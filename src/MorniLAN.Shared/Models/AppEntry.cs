namespace MorniLAN.Shared.Models;

/// <summary>Woher ein App-Eintrag stammt.</summary>
public enum AppSource
{
    /// <summary>Installiertes Programm (Uninstall-Registry / Startmenü).</summary>
    InstalledProgram,

    /// <summary>Steam-Spiel aus appmanifest_*.acf.</summary>
    Steam,

    /// <summary>Vom Admin manuell angelegter Eintrag (EXE-Pfad).</summary>
    Custom,
}

/// <summary>
/// Ein Programm oder Spiel auf dem Freundes-PC, das freigegeben werden kann.
/// </summary>
/// <param name="Id">Stabile ID, siehe <see cref="AppId"/>.</param>
/// <param name="Name">Anzeigename.</param>
/// <param name="Source">Herkunft des Eintrags.</param>
/// <param name="ExecutablePath">Pfad zur EXE (bei Steam leer, Start über steam://).</param>
/// <param name="SteamAppId">Steam-AppID, nur bei <see cref="AppSource.Steam"/>.</param>
/// <param name="SizeBytes">Installationsgröße, falls bekannt.</param>
/// <param name="IconPng">Icon als PNG (klein, z. B. 64×64), falls vorhanden.</param>
public sealed record AppEntry(
    string Id,
    string Name,
    AppSource Source,
    string? ExecutablePath = null,
    int? SteamAppId = null,
    long? SizeBytes = null,
    byte[]? IconPng = null)
{
    /// <summary>Startbefehl für den Launcher.</summary>
    public string? LaunchTarget => Source == AppSource.Steam && SteamAppId is { } appId
        ? $"steam://rungameid/{appId}"
        : ExecutablePath;
}
