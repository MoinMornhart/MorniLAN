using System.Text.RegularExpressions;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Inventory;

/// <summary>Woraus das Icon bzw. Cover eines Eintrags erzeugt wird (Datei auf dem PC).</summary>
internal sealed record ImageSources(string? IconFromExecutable = null, string? IconFile = null, string? CoverFile = null);

/// <summary>Ein Eintrag samt den Dateien, aus denen seine Bilder entstehen.</summary>
internal sealed record InventoryItem(AppEntry App, ImageSources Images);

/// <summary>
/// Führt Steam-Spiele, Uninstall-Einträge, Startmenü-Verknüpfungen und Store-Apps zu einer Liste zusammen.
/// <list type="bullet">
/// <item>Steam-Spiele kommen nur einmal (nicht zusätzlich als "Steam App 123" oder über ihre Verknüpfung).</item>
/// <item>Verknüpfungen werden Programmen zugeordnet: zuerst über den Ordner (Installationsort, Ordner des Icons
///   oder Uninstallers; der genaueste gewinnt), dann über den Namen.</item>
/// <item>Ein Programm mit genau einer Verknüpfung wird ein Eintrag mit deren EXE; mit mehreren (Office, Git)
///   wird jede Verknüpfung ein eigener Eintrag; ohne startbare EXE gilt es als Systemkomponente.</item>
/// <item>Die ID hängt an der EXE, sonst am Uninstall-Schlüssel.</item>
/// </list>
/// </summary>
internal static partial class InventoryBuilder
{
    public static IReadOnlyList<InventoryItem> Build(
        IReadOnlyList<SteamGame> steamGames,
        IReadOnlyList<UninstallEntry> uninstallEntries,
        IReadOnlyList<StartMenuShortcut> shortcuts,
        IReadOnlyList<StoreApp> storeApps)
    {
        var items = new Dictionary<string, InventoryItem>(StringComparer.OrdinalIgnoreCase);
        void Add(InventoryItem item) => items.TryAdd(item.App.Id, item);

        foreach (var game in steamGames)
        {
            Add(new InventoryItem(
                new AppEntry(AppId.ForSteam(game.AppId), game.Name, AppSource.Steam, SteamAppId: game.AppId,
                    SizeBytes: game.SizeOnDisk > 0 ? game.SizeOnDisk : null),
                new ImageSources(IconFile: game.IconPath, CoverFile: game.CoverPath)));
        }
        var steamFolders = steamGames.Select(g => g.InstallDirectory).ToList();
        bool InSteamGame(string? path) => path is not null && steamFolders.Any(f => PathText.IsUnder(path, f));

        var programs = uninstallEntries.Where(e => !SteamAppKey().IsMatch(e.KeyName)).ToList();
        var links = shortcuts
            .Select(s => s with { TargetPath = PathText.Normalize(s.TargetPath) })
            .Where(s => !InSteamGame(s.TargetPath))
            .ToList();
        var assignment = Assign(programs, links);

        foreach (var entry in programs)
        {
            var own = assignment.Where(a => a.Value == entry).Select(a => a.Key).ToList();
            var system = SystemComponents.IsSystem(entry);
            if (own.Count >= 2)
            {
                // Paket mit mehreren Programmen (Office, Git, …): jedes einzeln anzeigen
                foreach (var link in own)
                    Add(FromShortcut(link, entry.Publisher, entry.Version, system));
                continue;
            }

            var shortcut = own.SingleOrDefault();
            var exe = shortcut?.TargetPath ?? PathText.ExecutableFromIcon(entry.DisplayIcon);
            if (PathText.IsUninstaller(exe))
                exe = null;
            if (InSteamGame(exe))
                continue;

            Add(new InventoryItem(
                new AppEntry(exe is null ? AppId.ForUninstallKey(entry.KeyName) : AppId.ForExecutable(exe),
                    entry.DisplayName, AppSource.InstalledProgram, ExecutablePath: exe, SizeBytes: entry.EstimatedSizeBytes,
                    Publisher: entry.Publisher, Version: entry.Version,
                    // ohne startbare EXE kann der Launcher es ohnehin nicht öffnen
                    IsSystemComponent: system || exe is null || SystemComponents.IsSystemPath(exe),
                    LaunchArguments: shortcut?.Arguments),
                new ImageSources(IconFromExecutable: exe ?? PathText.ExecutableFromIcon(entry.DisplayIcon))));
        }

        foreach (var link in links.Where(l => !assignment.ContainsKey(l)))
            Add(FromShortcut(link, null, null, false));

        foreach (var app in storeApps)
        {
            Add(new InventoryItem(
                new AppEntry(AppId.ForStoreApp(app.AppUserModelId), app.Name, AppSource.StoreApp, Publisher: app.Publisher,
                    Version: app.Version, StoreAppUserModelId: app.AppUserModelId, SizeBytes: app.SizeBytes,
                    IsSystemComponent: SystemComponents.IsSystemStoreApp(app)),
                new ImageSources(IconFile: app.LogoPath)));
        }

        // Manche Hersteller (Adobe) legen zusätzlich einen Eintrag ohne EXE an: weg, wenn es das Programm startbar gibt
        var launchable = items.Values.Where(i => i.App.LaunchTarget is not null)
            .Select(i => PathText.NameKey(i.App.Name)).ToHashSet();
        return [.. items.Values
            .Where(i => i.App.LaunchTarget is not null || !launchable.Contains(PathText.NameKey(i.App.Name)))
            .OrderBy(i => i.App.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static InventoryItem FromShortcut(StartMenuShortcut link, string? publisher, string? version, bool system) =>
        new(new AppEntry(AppId.ForExecutable(link.TargetPath), link.Name, AppSource.InstalledProgram,
                ExecutablePath: link.TargetPath, Publisher: publisher, Version: version,
                IsSystemComponent: system || SystemComponents.IsSystemPath(link.TargetPath), LaunchArguments: link.Arguments),
            new ImageSources(IconFromExecutable: link.TargetPath));

    /// <summary>Ordnet jede Verknüpfung höchstens einem Programm zu.</summary>
    private static Dictionary<StartMenuShortcut, UninstallEntry> Assign(List<UninstallEntry> programs, List<StartMenuShortcut> links)
    {
        var result = new Dictionary<StartMenuShortcut, UninstallEntry>();
        foreach (var link in links)
        {
            // 1. Ordner: der längste (genaueste) passende Ordner gewinnt
            var byFolder = programs
                .SelectMany(p => Folders(p).Where(f => PathText.IsUnder(link.TargetPath, f)).Select(f => (Program: p, f.Length)))
                .OrderByDescending(x => x.Length)
                .Select(x => x.Program)
                .FirstOrDefault();
            if (byFolder is not null)
            {
                result[link] = byFolder;
                continue;
            }

            // 2. Name: exakt gleich vor "beginnt mit", bei Gleichstand der kürzere Programmname
            var byName = programs
                .Where(p => PathText.NamesMatch(p.DisplayName, link.Name))
                .OrderByDescending(p => PathText.NameKey(p.DisplayName) == PathText.NameKey(link.Name))
                .ThenBy(p => p.DisplayName.Length)
                .FirstOrDefault();
            if (byName is not null)
                result[link] = byName;
        }
        return result;
    }

    /// <summary>Ordner, die ein Programm verraten: Installationsort, Ordner des Icons und des Uninstallers.</summary>
    private static IEnumerable<string> Folders(UninstallEntry entry)
    {
        if (!PathText.IsGenericFolder(entry.InstallLocation))
            yield return entry.InstallLocation!;
        foreach (var exe in new[] { PathText.ExecutableFromIcon(entry.DisplayIcon), PathText.ExecutableFromCommand(entry.UninstallString) })
        {
            var folder = exe is null ? null : Path.GetDirectoryName(exe);
            if (!PathText.IsGenericFolder(folder))
                yield return folder!;
        }
    }

    [GeneratedRegex(@"^Steam App \d+$", RegexOptions.IgnoreCase)]
    private static partial Regex SteamAppKey();
}
