using System.Security.Cryptography;
using System.Text;
using MorniLAN.Agent.Inventory;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Policy;

/// <summary>
/// Was der Launcher zeigt: alle erkannten und eigenen Einträge, die startbar und keine Systemkomponente sind und
/// für mindestens ein Profil (oder ohne Profile für den PC) frei sind. Für wen ein Eintrag gesperrt ist, steht in
/// <see cref="LauncherApp.HiddenFor"/>. Wird nur neu gebaut, wenn sich Liste, Freigaben oder Profile ändern.
/// </summary>
internal sealed class LauncherCatalog(InventoryService inventory, AgentPolicyStore policy, AgentProfileStore? profiles = null)
{
    private readonly Lock _lock = new();
    private (string? Inventory, long Revision, LauncherProfile[] Profiles, LauncherAppList List)? _cached;

    public LauncherAppList Current()
    {
        var report = inventory.Current;
        var current = policy.Current;
        var people = profiles?.Current ?? [];
        lock (_lock)
        {
            if (_cached is { } c && c.Inventory == report?.ContentHash && c.Revision == current.Revision
                && ReferenceEquals(c.Profiles, people))
                return c.List;
            var list = Build(report?.Apps ?? [], current, inventory.GetImage, people);
            _cached = (report?.ContentHash, current.Revision, people, list);
            return list;
        }
    }

    internal static LauncherAppList Build(IEnumerable<AppEntry> apps, AppPolicy policy, Func<string, AppImage?> image,
        LauncherProfile[]? profiles = null)
    {
        var people = profiles ?? [];
        // Ohne Profile gilt der PC, mit Profilen nur noch die Profile
        string?[] audience = people.Length == 0 ? [null] : [.. people.Select(p => p.Id)];
        var tiles = apps
            .Where(a => !a.IsSystemComponent && a.LaunchTarget is not null)
            .Select(a => (App: a, Hidden: audience.Where(p => !policy.IsAllowed(a.Id, p)).Select(p => p ?? LauncherAppList.NoProfile).ToArray()))
            .Where(x => x.Hidden.Length < audience.Length)
            .OrderByDescending(x => x.App.IsGame)
            .ThenBy(x => x.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new LauncherApp(x.App.Id, x.App.Name, x.App.LaunchTarget!,
                // Bei Launcher-Links stecken die Argumente schon im Link
                x.App.LaunchUri is null && x.App.Source is AppSource.InstalledProgram or AppSource.Custom ? x.App.LaunchArguments : null,
                x.App.IsGame,
                x.App.IconHash is { } icon ? image(icon)?.Data : null,
                x.App.IsGame && x.App.CoverHash is { } cover ? image(cover)?.Data : null,
                x.Hidden.Length > 0 ? x.Hidden : null))
            .ToArray();
        return new LauncherAppList(Hash(tiles, people, policy.BlockProfileCreation), tiles, people, policy.BlockProfileCreation);
    }

    private static string Hash(LauncherApp[] apps, LauncherProfile[] profiles, bool creationBlocked)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var app in apps)
        {
            sha.AppendData(Encoding.UTF8.GetBytes($"{app.Id}\n{app.Name}\n{app.LaunchTarget}\n{app.Arguments}\n{string.Join(',', app.HiddenFor ?? [])}\n"));
            sha.AppendData(app.Icon ?? []);
            sha.AppendData(app.Cover ?? []);
        }
        foreach (var profile in profiles)
            sha.AppendData(Encoding.UTF8.GetBytes(
                $"P{profile.Id}\n{profile.Name}\n{profile.Color}\n{profile.Theme}\n{profile.HasPassword}\n{profile.HasCustomBackground}\n{profile.HasAvatar}\n"));
        sha.AppendData(creationBlocked ? [1] : [0]);
        return Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }
}
