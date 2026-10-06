using System.Security.Cryptography;
using System.Text;
using MorniLAN.Agent.Inventory;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Policy;

/// <summary>
/// Was der Launcher zeigt: alle erkannten und eigenen Einträge, die freigegeben, startbar und keine
/// Systemkomponente sind. Wird nur neu gebaut, wenn sich Programmliste oder Freigaben geändert haben.
/// </summary>
internal sealed class LauncherCatalog(InventoryService inventory, AgentPolicyStore policy)
{
    private readonly Lock _lock = new();
    private (string? Inventory, long Revision, LauncherAppList List)? _cached;

    public LauncherAppList Current()
    {
        var report = inventory.Current;
        var current = policy.Current;
        lock (_lock)
        {
            if (_cached is { } c && c.Inventory == report?.ContentHash && c.Revision == current.Revision)
                return c.List;
            var list = Build(report?.Apps ?? [], current, inventory.GetImage);
            _cached = (report?.ContentHash, current.Revision, list);
            return list;
        }
    }

    internal static LauncherAppList Build(IEnumerable<AppEntry> apps, AppPolicy policy, Func<string, AppImage?> image)
    {
        var tiles = apps
            .Where(a => !a.IsSystemComponent && a.LaunchTarget is not null && policy.IsAllowed(a.Id))
            .OrderByDescending(a => a.IsGame)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(a => new LauncherApp(a.Id, a.Name, a.LaunchTarget!,
                // Bei Launcher-Links stecken die Argumente schon im Link
                a.LaunchUri is null && a.Source is AppSource.InstalledProgram or AppSource.Custom ? a.LaunchArguments : null,
                a.IsGame,
                a.IconHash is { } icon ? image(icon)?.Data : null,
                a.IsGame && a.CoverHash is { } cover ? image(cover)?.Data : null))
            .ToArray();
        return new LauncherAppList(Hash(tiles), tiles);
    }

    private static string Hash(LauncherApp[] apps)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var app in apps)
        {
            sha.AppendData(Encoding.UTF8.GetBytes($"{app.Id}\n{app.Name}\n{app.LaunchTarget}\n{app.Arguments}\n"));
            sha.AppendData(app.Icon ?? []);
            sha.AppendData(app.Cover ?? []);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }
}
