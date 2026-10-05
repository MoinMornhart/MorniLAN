using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MorniLAN.Agent.Inventory;

/// <summary>Ein installiertes Steam-Spiel.</summary>
/// <param name="CoverPath">Hochformat-Cover (600×900), sonst Querformat-Header als Ersatz.</param>
internal sealed record SteamGame(int AppId, string Name, string InstallDirectory, long SizeOnDisk, string? CoverPath,
    string? IconPath);

/// <summary>
/// Findet Steam-Spiele über libraryfolders.vdf und appmanifest_*.acf. Liest keine Zugangsdaten
/// (loginusers.vdf, config.vdf werden nicht angefasst).
/// </summary>
internal static partial class SteamScanner
{
    /// <summary>Steam-Einträge, die keine Spiele sind.</summary>
    private static readonly HashSet<int> IgnoredAppIds =
    [
        228980, // Steamworks Common Redistributables
        1070560, 1391110, 1628350, // Steam Linux Runtime
        1493710, 2180100, 2348590, // Proton
    ];

    private const int StateFullyInstalled = 4;

    /// <summary>Steam-Installation aus der Registry (maschinenweit, funktioniert auch als Dienst).</summary>
    public static string? FindSteamRoot()
    {
        foreach (var keyPath in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("InstallPath") is string path && Directory.Exists(path))
                return path;
        }
        return null;
    }

    public static IReadOnlyList<SteamGame> Scan(string steamRoot)
    {
        var games = new Dictionary<int, SteamGame>();
        foreach (var library in LibraryFolders(steamRoot))
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
                continue;
            foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                if (TryReadManifest(manifest, steamRoot, out var game) && !games.ContainsKey(game.AppId))
                    games[game.AppId] = game;
            }
        }
        return [.. games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Alle Bibliotheksordner, der Steam-Ordner selbst zuerst.</summary>
    internal static IReadOnlyList<string> LibraryFolders(string steamRoot)
    {
        var folders = new List<string> { steamRoot };
        var file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file))
            return folders;

        VdfNode root;
        try { root = Vdf.ParseFile(file); }
        catch (Exception ex) when (ex is FormatException or IOException) { return folders; }

        var list = root.Child("libraryfolders") ?? root.Child("LibraryFolders");
        foreach (var (_, value) in list?.Entries ?? [])
        {
            // Neues Format: "0" { "path" "…" }, altes Format: "1" "D:\\SteamLibrary"
            var path = value switch
            {
                VdfNode node => node["path"],
                string text when text.Contains('\\') || text.Contains(':') => text,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                folders.Add(path);
        }
        return folders;
    }

    internal static bool TryReadManifest(string manifestPath, string steamRoot, out SteamGame game)
    {
        game = null!;
        VdfNode root;
        try { root = Vdf.ParseFile(manifestPath); }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException) { return false; }

        var state = root.Child("AppState");
        if (state is null
            || !int.TryParse(state["appid"], NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
            || IgnoredAppIds.Contains(appId)
            || string.IsNullOrWhiteSpace(state["name"]))
            return false;

        // Nur fertig installierte Spiele (nicht "wird heruntergeladen" oder "deinstalliert")
        if (!int.TryParse(state["StateFlags"], NumberStyles.None, CultureInfo.InvariantCulture, out var flags)
            || (flags & StateFullyInstalled) == 0)
            return false;

        long.TryParse(state["SizeOnDisk"], NumberStyles.None, CultureInfo.InvariantCulture, out var size);
        var library = Path.GetDirectoryName(manifestPath)!; // …\steamapps
        var installDir = Path.Combine(library, "common", state["installdir"] ?? string.Empty);
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        game = new SteamGame(appId, state["name"]!.Trim(), installDir, size, FindCover(cache, appId), FindIcon(cache, appId));
        return true;
    }

    /// <summary>
    /// Cover im Steam-Cache. Neues Layout: librarycache\&lt;appid&gt;\library_600x900.jpg (auch in Unterordnern),
    /// altes Layout: librarycache\&lt;appid&gt;_library_600x900.jpg. Ersatzweise der Querformat-Header.
    /// </summary>
    internal static string? FindCover(string libraryCache, int appId)
    {
        var folder = Path.Combine(libraryCache, appId.ToString(CultureInfo.InvariantCulture));
        return FirstExisting(folder, "library_600x900.jpg")
               ?? FirstExisting(libraryCache, $"{appId}_library_600x900.jpg")
               ?? FirstExisting(folder, "header.jpg")
               ?? FirstExisting(folder, "library_header.jpg")
               ?? FirstExisting(libraryCache, $"{appId}_header.jpg");
    }

    /// <summary>Kleines Spiel-Icon: im neuen Layout eine Datei mit 40-stelligem Hash-Namen direkt im App-Ordner.</summary>
    internal static string? FindIcon(string libraryCache, int appId)
    {
        var folder = Path.Combine(libraryCache, appId.ToString(CultureInfo.InvariantCulture));
        if (Directory.Exists(folder))
        {
            var icon = Directory.EnumerateFiles(folder, "*.jpg")
                .FirstOrDefault(f => HashName().IsMatch(Path.GetFileNameWithoutExtension(f)));
            if (icon is not null)
                return icon;
        }
        return FirstExisting(libraryCache, $"{appId}_icon.jpg");
    }

    private static string? FirstExisting(string folder, string fileName)
    {
        if (!Directory.Exists(folder))
            return null;
        var direct = Path.Combine(folder, fileName);
        if (File.Exists(direct))
            return direct;
        return Directory.EnumerateFiles(folder, fileName, SearchOption.AllDirectories).FirstOrDefault();
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase)]
    private static partial Regex HashName();
}
