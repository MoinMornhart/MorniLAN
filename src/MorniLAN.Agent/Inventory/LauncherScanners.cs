using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Inventory;

/// <summary>Ein Spiel aus Epic, GOG oder Ubisoft Connect.</summary>
/// <param name="LauncherId">ID im Launcher (Epic: AppName, GOG: gameID, Ubisoft: Install-ID).</param>
/// <param name="InstallDirectory">Spielordner: Verknüpfungen und Programmeinträge darin gehören zum Spiel.</param>
internal sealed record LauncherGame(
    string Launcher,
    string IdPrefix,
    string LauncherId,
    string Name,
    string InstallDirectory,
    string? ExecutablePath = null,
    string? LaunchUri = null,
    string? LaunchArguments = null,
    long? SizeBytes = null,
    string? IconFile = null,
    string? CoverFile = null);

/// <summary>
/// Epic Games Launcher: eine JSON-Datei (*.item) je installiertem Spiel unter
/// %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests. Gestartet wird über den Launcher (Anmeldung, Updates).
/// </summary>
internal static class EpicScanner
{
    public static string? FindManifestFolder()
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
            .OpenSubKey(@"SOFTWARE\Epic Games\EpicGamesLauncher");
        var data = key?.GetValue("AppDataPath") as string
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                       @"Epic\EpicGamesLauncher\Data");
        var folder = Path.Combine(data, "Manifests");
        return Directory.Exists(folder) ? folder : null;
    }

    public static IReadOnlyList<LauncherGame> Scan(string manifestFolder)
    {
        var games = new List<LauncherGame>();
        foreach (var file in Directory.EnumerateFiles(manifestFolder, "*.item"))
        {
            try
            {
                if (Parse(File.ReadAllText(file)) is { } game)
                    games.Add(game);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // kaputtes Manifest: dieses Spiel auslassen
            }
        }
        return [.. games.DistinctBy(g => g.LauncherId, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Ein Manifest. null für Erweiterungen (DLC), Engines, Plugins und halb installierte Spiele.</summary>
    internal static LauncherGame? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? Text(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!.Trim()
                : null;

        var appName = Text("AppName");
        var name = Text("DisplayName");
        var folder = Text("InstallLocation");
        if (appName is null || name is null || folder is null)
            return null;
        if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
            return null;
        // DLC verweist auf sein Hauptspiel
        if (Text("MainGameAppName") is { } main && !main.Equals(appName, StringComparison.OrdinalIgnoreCase))
            return null;
        if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array
            && !categories.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "games"))
            return null;

        var ns = Text("CatalogNamespace");
        var item = Text("CatalogItemId");
        var uri = ns is not null && item is not null
            ? $"com.epicgames.launcher://apps/{Uri.EscapeDataString(ns)}%3A{Uri.EscapeDataString(item)}%3A{Uri.EscapeDataString(appName)}?action=launch&silent=true"
            : $"com.epicgames.launcher://apps/{Uri.EscapeDataString(appName)}?action=launch&silent=true";
        var exe = Text("LaunchExecutable") is { } relative ? Path.GetFullPath(Path.Combine(folder, relative)) : null;
        long? size = root.TryGetProperty("InstallSize", out var s) && s.TryGetInt64(out var bytes) && bytes > 0 ? bytes : null;

        return new LauncherGame(GameLaunchers.Epic, "epic", appName, name, folder, exe, uri, SizeBytes: size);
    }
}

/// <summary>
/// GOG (mit oder ohne Galaxy): je Spiel ein Schlüssel unter HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\&lt;gameID&gt;.
/// GOG-Spiele sind DRM-frei und starten direkt über ihre EXE.
/// </summary>
internal static class GogScanner
{
    public static IReadOnlyList<LauncherGame> Scan()
    {
        using var games = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
            .OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (games is null)
            return [];
        var result = new List<LauncherGame>();
        foreach (var id in games.GetSubKeyNames())
        {
            using var key = games.OpenSubKey(id);
            if (key is not null && Parse(id, name => key.GetValue(name) as string) is { } game)
                result.Add(game);
        }
        return result;
    }

    /// <summary>Werte eines Spiel-Schlüssels. null für DLC (hängt von einem anderen Spiel ab) und Unvollständiges.</summary>
    internal static LauncherGame? Parse(string keyName, Func<string, string?> value)
    {
        string? Text(string name) => value(name) is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var id = Text("gameID") ?? keyName;
        var name = Text("gameName");
        var folder = Text("path");
        var exe = Text("exe");
        if (name is null || folder is null || exe is null || Text("dependsOn") is not null)
            return null;

        var icon = Path.Combine(folder, $"goggame-{id}.ico");
        return new LauncherGame(GameLaunchers.Gog, "gog", id, name, folder, exe,
            LaunchArguments: Text("launchParam"),
            IconFile: File.Exists(icon) ? icon : null);
    }
}

/// <summary>
/// Ubisoft Connect: Spielordner unter HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\&lt;id&gt;, der Name steht
/// im Programmeintrag „Uplay Install &lt;id&gt;“. Gestartet wird über uplay://launch/&lt;id&gt;/0.
/// </summary>
internal static class UbisoftScanner
{
    public static IReadOnlyList<LauncherGame> Scan()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var installs = hklm.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
        if (installs is null)
            return [];
        var result = new List<LauncherGame>();
        foreach (var id in installs.GetSubKeyNames())
        {
            using var key = installs.OpenSubKey(id);
            using var uninstall = hklm.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install {id}");
            if (Parse(id, key?.GetValue("InstallDir") as string, uninstall?.GetValue("DisplayName") as string,
                    uninstall?.GetValue("DisplayIcon") as string) is { } game)
                result.Add(game);
        }
        return result;
    }

    internal static LauncherGame? Parse(string id, string? installDir, string? displayName, string? displayIcon)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _) || string.IsNullOrWhiteSpace(installDir))
            return null;
        // Ubisoft schreibt Pfade mit "/" und Schrägstrich am Ende
        var folder = installDir.Trim().Replace('/', '\\').TrimEnd('\\');
        var name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(folder) : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var exe = PathText.ExecutableFromIcon(displayIcon);
        return new LauncherGame(GameLaunchers.Ubisoft, "ubisoft", id, name, folder, exe, $"uplay://launch/{id}/0");
    }
}

/// <summary>
/// EA app und Battle.net legen keine eigene, gut lesbare Spieleliste ab, ihre Spiele stehen aber in der
/// Windows-Programmliste. Erkannt werden sie am Deinstallationsprogramm des Launchers; gestartet wird die EXE
/// bzw. die Verknüpfung (die öffnet bei Bedarf den Launcher).
/// </summary>
internal static class LauncherDetection
{
    public static string? ForUninstallEntry(UninstallEntry entry)
    {
        var uninstall = entry.UninstallString ?? "";
        // EA: "C:\Program Files\Common Files\EAInstaller\<Spiel>\Cleanup.exe" uninstall_game
        if (uninstall.Contains(@"\EAInstaller\", StringComparison.OrdinalIgnoreCase))
            return GameLaunchers.Ea;
        // Blizzard: "C:\ProgramData\Battle.net\Agent\Blizzard Uninstaller.exe" --uid=wow …; nicht Battle.net selbst
        if (uninstall.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase)
            && !uninstall.Contains("--uid=battle.net", StringComparison.OrdinalIgnoreCase)
            && !entry.DisplayName.Equals("Battle.net", StringComparison.OrdinalIgnoreCase))
            return GameLaunchers.BattleNet;
        return null;
    }
}
