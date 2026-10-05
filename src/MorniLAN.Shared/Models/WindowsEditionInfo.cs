namespace MorniLAN.Shared.Models;

/// <summary>Grobe Edition-Familie, entscheidet über verfügbare Sperr- und Fernzugriffsmechanismen.</summary>
public enum EditionFamily
{
    Unknown,
    Home,
    Pro,
    Enterprise,
    Education,
    Server,
}

/// <summary>Mechanismus, mit dem nicht freigegebene Programme blockiert werden.</summary>
public enum LockdownMechanism
{
    /// <summary>Nur Prozess-Wächter (beendet nicht erlaubte Prozesse).</summary>
    ProcessWatcher,

    /// <summary>App Control for Business (WDAC), auf allen Editionen verfügbar.</summary>
    AppControl,

    /// <summary>AppLocker, offiziell nur Enterprise/Education/Server.</summary>
    AppLocker,
}

/// <summary>
/// Windows-Version aus HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion.
/// </summary>
public sealed record WindowsEditionInfo(string EditionId, string ProductName, string DisplayVersion, int Build)
{
    public EditionFamily Family => ClassifyEdition(EditionId);

    /// <summary>
    /// Anzeigename. Windows 11 meldet in der Registry weiterhin "Windows 10 …" als ProductName,
    /// das wird hier anhand der Build-Nummer korrigiert.
    /// </summary>
    public string FriendlyName
    {
        get
        {
            var name = Build >= 22000 && ProductName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)
                ? "Windows 11" + ProductName["Windows 10".Length..]
                : ProductName;
            return string.IsNullOrWhiteSpace(DisplayVersion) ? name : $"{name} {DisplayVersion}";
        }
    }

    /// <summary>Kann dieser PC als RDP-Host dienen? (Home: nein)</summary>
    public bool SupportsRdpHost => Family is not (EditionFamily.Home or EditionFamily.Unknown);

    /// <summary>Bevorzugter Sperr-Mechanismus. Der Prozess-Wächter läuft immer zusätzlich als Fallback.</summary>
    public LockdownMechanism PreferredLockdown => Family switch
    {
        EditionFamily.Enterprise or EditionFamily.Education or EditionFamily.Server => LockdownMechanism.AppLocker,
        EditionFamily.Home or EditionFamily.Pro when Build >= 18362 => LockdownMechanism.AppControl,
        _ => LockdownMechanism.ProcessWatcher,
    };

    public static EditionFamily ClassifyEdition(string? editionId)
    {
        if (string.IsNullOrWhiteSpace(editionId))
            return EditionFamily.Unknown;
        var id = editionId.Trim();
        if (id.StartsWith("Server", StringComparison.OrdinalIgnoreCase))
            return EditionFamily.Server;
        if (id.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
            return EditionFamily.Home;
        // "ProfessionalEducation" ist Pro Education, zählt als Education.
        if (id.Contains("Education", StringComparison.OrdinalIgnoreCase))
            return EditionFamily.Education;
        if (id.StartsWith("Professional", StringComparison.OrdinalIgnoreCase))
            return EditionFamily.Pro;
        if (id.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
            return EditionFamily.Enterprise;
        return EditionFamily.Unknown;
    }
}
