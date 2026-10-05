using System.Globalization;
using Microsoft.Win32;

namespace MorniLAN.Agent.Inventory;

/// <summary>Ein Eintrag aus „Apps &amp; Features“ (Uninstall-Schlüssel der Registry).</summary>
internal sealed record UninstallEntry(
    string KeyName,
    string DisplayName,
    string? Publisher = null,
    string? Version = null,
    string? InstallLocation = null,
    string? DisplayIcon = null,
    long? EstimatedSizeBytes = null,
    bool SystemComponent = false,
    string? ParentKeyName = null,
    string? ReleaseType = null,
    string? UninstallString = null);

/// <summary>
/// Liest die Uninstall-Schlüssel: maschinenweit (64- und 32-Bit-Ansicht) und für jedes angemeldete
/// Benutzerkonto (HKEY_USERS\&lt;SID&gt;). Als Dienst (SYSTEM) sieht der Agent so auch die Programme,
/// die der Freund nur für sich installiert hat (z. B. Discord).
/// </summary>
internal static class UninstallRegistryScanner
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<UninstallEntry> Scan()
    {
        var entries = new List<UninstallEntry>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            ReadUninstallKeys(hklm, entries);
        }

        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default);
        foreach (var sid in users.GetSubKeyNames().Where(IsUserSid))
        {
            try
            {
                using var hive = users.OpenSubKey(sid);
                if (hive is not null)
                    ReadUninstallKeys(hive, entries);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                // fremdes Profil im Konsolenmodus nicht lesbar: überspringen
            }
        }

        // Derselbe Schlüssel kann in mehreren Ansichten auftauchen
        return [.. entries.DistinctBy(e => (e.KeyName.ToUpperInvariant(), e.DisplayName.ToUpperInvariant()))];
    }

    /// <summary>Echte Benutzerkonten (S-1-5-21-…), nicht deren _Classes-Hives und nicht Dienstkonten.</summary>
    internal static bool IsUserSid(string name) =>
        name.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)
        && !name.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase);

    private static void ReadUninstallKeys(RegistryKey root, List<UninstallEntry> target)
    {
        using var uninstall = root.OpenSubKey(UninstallPath);
        if (uninstall is null)
            return;
        foreach (var name in uninstall.GetSubKeyNames())
        {
            try
            {
                using var key = uninstall.OpenSubKey(name);
                if (key?.GetValue("DisplayName") is not string displayName || string.IsNullOrWhiteSpace(displayName))
                    continue;
                target.Add(new UninstallEntry(
                    name,
                    displayName.Trim(),
                    Text(key, "Publisher"),
                    Text(key, "DisplayVersion"),
                    Text(key, "InstallLocation"),
                    Text(key, "DisplayIcon"),
                    key.GetValue("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : null,
                    key.GetValue("SystemComponent") is int sc && sc == 1,
                    Text(key, "ParentKeyName"),
                    Text(key, "ReleaseType"),
                    Text(key, "UninstallString")));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }
    }

    private static string? Text(RegistryKey key, string name) =>
        key.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>Erkennt Systemkomponenten, die im Panel standardmäßig ausgeblendet werden.</summary>
internal static class SystemComponents
{
    private static readonly string[] NamePatterns =
    [
        "Redistributable", "Microsoft Visual C++", "Microsoft .NET", ".NET Runtime", ".NET SDK", ".NET Desktop Runtime",
        ".NET Host", "ASP.NET Core", "Windows Desktop Runtime", "Windows SDK", "Windows Software Development Kit",
        "Windows Driver", "Microsoft Edge WebView2", "Microsoft Update Health Tools", "Microsoft GameInput",
        "Update for ", "Security Update", "Hotfix", "Service Pack", "DirectX", "Vulkan Run", "PhysX", "Driver", "Treiber",
        "Chipset", "Bonjour", "Microsoft Windows Desktop Runtime", "vs_", "Microsoft Visual Studio Installer",
        "Windows App Runtime", "Microsoft Windows App", "Teams Machine-Wide Installer", "Office 16 Click-to-Run",
        "Microsoft Search in Bing", "Windows PC Health Check", "Intel(R) Management Engine", "Intel(R) Serial IO",
        "Realtek", "Killer Performance", "Killer Control Center", "NVIDIA FrameView", "NVIDIA HD Audio",
        "NVIDIA Graphics", "NVIDIA USBC", "AMD Chipset", "Dolby", "Synaptics", "ELAN",
    ];

    private static readonly string[] UpdateReleaseTypes = ["Update", "Hotfix", "Security Update", "ServicePack"];

    public static bool IsSystem(UninstallEntry entry) =>
        entry.SystemComponent
        || entry.ParentKeyName is not null
        || (entry.ReleaseType is { } type && UpdateReleaseTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        || NamePatterns.Any(p => entry.DisplayName.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Programme im Windows-Ordner (Systemsteuerung, Remotedesktop, …) zählen als System, MorniLAN selbst auch.</summary>
    public static bool IsSystemPath(string? path) =>
        path is not null
        && ((Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } windows
             && path.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            || Path.GetFileName(path).StartsWith("MorniLAN.", StringComparison.OrdinalIgnoreCase));

    /// <summary>Store-Apps von Hardware-Herstellern und Windows-Werkzeuge, die der Freund nicht braucht.</summary>
    private static readonly string[] StoreFamilyPrefixes =
    [
        "Microsoft.WindowsFeedbackHub", "Microsoft.GetHelp", "Microsoft.SecHealthUI", "Microsoft.WindowsTerminal",
        "Microsoft.PowerAutomateDesktop", "Microsoft.MixedReality", "MicrosoftCorporationII.MicrosoftFamily",
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.Windows.DevHome",
        "Microsoft.MicrosoftEdge", "Microsoft.Winget", "MicrosoftWindows.Client", "MicrosoftWindows.CrossDevice",
        "Microsoft.Windows.Ai", "Microsoft.Copilot", "Microsoft.OutlookForWindows", "Microsoft.Office.ActionsServer",
    ];

    private static readonly string[] StorePublisherPatterns =
        ["Realtek", "Intel", "Rivet", "Killer", "DTS", "Dolby", "MICRO-STAR", "MSI", "Synaptics", "NVIDIA", "AMD", "ELAN",
         "Waves", "Nahimic", "Advanced Micro Devices"];

    public static bool IsSystemStoreApp(StoreApp app) =>
        StoreFamilyPrefixes.Any(p => app.AppUserModelId.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        || StorePublisherPatterns.Any(p => app.Publisher.Contains(p, StringComparison.OrdinalIgnoreCase)
                                           || app.AppUserModelId.Contains(p, StringComparison.OrdinalIgnoreCase))
        || NamePatterns.Any(p => app.Name.Contains(p, StringComparison.OrdinalIgnoreCase));
}

internal static class PathText
{
    /// <summary>"\"C:\x\app.exe\",0" → C:\x\app.exe; null, wenn es keine .exe ist.</summary>
    public static string? ExecutableFromIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
            return null;
        var text = displayIcon.Trim();
        var comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            text = text[..comma];
        text = text.Trim().Trim('"');
        return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Normalize(Environment.ExpandEnvironmentVariables(text)) : null;
    }

    /// <summary>EXE aus einem Befehl wie "\"C:\x\unins000.exe\" /SILENT" oder C:\x\Update.exe --uninstall.</summary>
    public static string? ExecutableFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        var text = command.Trim();
        string candidate;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            candidate = end > 1 ? text[1..end] : text.Trim('"');
        }
        else
        {
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            candidate = exe > 0 ? text[..(exe + 4)] : text;
        }
        return candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Normalize(Environment.ExpandEnvironmentVariables(candidate))
            : null;
    }

    /// <summary>Uninstaller und Setup-Programme sind keine startbaren Apps.</summary>
    public static bool IsUninstaller(string? path)
    {
        if (path is null)
            return false;
        var file = Path.GetFileNameWithoutExtension(path);
        return file.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
               || file.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
               || file.StartsWith("setup", StringComparison.OrdinalIgnoreCase)
               || file.EndsWith("setup", StringComparison.OrdinalIgnoreCase)
               || file.Equals("msiexec", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnder(string path, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return false;
        var normalized = Normalize(folder.Trim().Trim('"')).TrimEnd('\\') + "\\";
        return normalized.Length > 3 && Normalize(path).StartsWith(normalized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Doppelte Backslashes und "..\" auflösen, damit derselbe Pfad dieselbe ID bekommt.</summary>
    public static string Normalize(string path)
    {
        try { return Path.GetFullPath(path.Replace('/', '\\')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    /// <summary>
    /// Ordner, die zu allgemein sind, um daraus auf ein Programm zu schließen (z. B. liegt jeder
    /// MSI-Uninstaller im Windows-Ordner, viele Setups im Package Cache).
    /// </summary>
    public static bool IsGenericFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return true;
        var f = Normalize(folder).TrimEnd('\\');
        string[] generic =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.GetPathRoot(f) ?? "",
        ];
        return generic.Any(g => g.Length > 0 && string.Equals(g.TrimEnd('\\'), f, StringComparison.OrdinalIgnoreCase))
               || SystemComponents.IsSystemPath(f + "\\x")
               || f.Contains(@"\Package Cache", StringComparison.OrdinalIgnoreCase)
               || f.Contains(@"\Installer", StringComparison.OrdinalIgnoreCase)
               || f.EndsWith(@"\AppData\Local", StringComparison.OrdinalIgnoreCase)
               || f.EndsWith(@"\AppData\Roaming", StringComparison.OrdinalIgnoreCase)
               || f.EndsWith(@"\AppData\Local\Programs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Name ohne Versionsnummern, Klammerzusätze und Architektur, für den Vergleich mit Verknüpfungen.</summary>
    public static string NameKey(string name)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(name, @"\([^)]*\)", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(v?\d+(\.\d+)+|x64|x86|64-bit|32-bit)\b", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return string.Join(' ', text.Split([' ', '-', '–'], StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    /// <summary>Gleicher Name oder einer beginnt mit dem anderen als ganzes Wort ("Oracle VirtualBox" ↔ "Oracle VirtualBox 7.2").</summary>
    public static bool NamesMatch(string a, string b)
    {
        var x = NameKey(a);
        var y = NameKey(b);
        if (x.Length < 3 || y.Length < 3)
            return false;
        return x == y || x.StartsWith(y + " ", StringComparison.Ordinal) || y.StartsWith(x + " ", StringComparison.Ordinal)
               || x.EndsWith(" " + y, StringComparison.Ordinal) || y.EndsWith(" " + x, StringComparison.Ordinal);
    }
}
