using System.Text.Json.Serialization;

namespace MorniLAN.Shared.Models;

/// <summary>Anzeigenamen der Launcher außer Steam, wie sie in <see cref="AppEntry.Launcher"/> stehen.</summary>
public static class GameLaunchers
{
    public const string Epic = "Epic Games";
    public const string Gog = "GOG";
    public const string Ubisoft = "Ubisoft Connect";
    public const string Ea = "EA app";
    public const string BattleNet = "Battle.net";
}

/// <summary>Woher ein App-Eintrag stammt.</summary>
public enum AppSource
{
    /// <summary>Installiertes Programm (Uninstall-Registry / Startmenü).</summary>
    InstalledProgram,

    /// <summary>Steam-Spiel aus appmanifest_*.acf.</summary>
    Steam,

    /// <summary>Vom Admin manuell angelegter Eintrag (EXE-Pfad).</summary>
    Custom,

    /// <summary>App aus dem Microsoft Store (MSIX/AppX), Start über die AppUserModelId.</summary>
    StoreApp,
}

/// <summary>
/// Ein Programm oder Spiel auf dem Freundes-PC, das freigegeben werden kann.
/// Bilder stecken nicht im Eintrag, sondern werden über ihren Hash einmalig übertragen (<see cref="AppImage"/>).
/// </summary>
/// <param name="Id">Stabile ID, siehe <see cref="AppId"/>.</param>
/// <param name="Name">Anzeigename.</param>
/// <param name="Source">Herkunft des Eintrags.</param>
/// <param name="ExecutablePath">Pfad zur EXE (bei Steam und Store-Apps leer).</param>
/// <param name="SteamAppId">Steam-AppID, nur bei <see cref="AppSource.Steam"/>.</param>
/// <param name="SizeBytes">Installationsgröße, falls bekannt.</param>
/// <param name="Publisher">Herausgeber, falls bekannt.</param>
/// <param name="Version">Version, falls bekannt.</param>
/// <param name="StoreAppUserModelId">AppUserModelId ("Familie!App"), nur bei <see cref="AppSource.StoreApp"/>.</param>
/// <param name="IsSystemComponent">Treiber, Laufzeitbibliotheken, Updates, Hilfsprogramme: im Panel standardmäßig ausgeblendet.</param>
/// <param name="IconHash">SHA-256 des Icons (PNG), falls vorhanden.</param>
/// <param name="CoverHash">SHA-256 des Coverbilds (z. B. Steam 600×900), falls vorhanden.</param>
/// <param name="LaunchArguments">Argumente aus der Verknüpfung (z. B. "--processStart Discord.exe" bei Updater-Apps).</param>
/// <param name="Launcher">
/// Spiel aus einem anderen Launcher als Steam (Werte aus <see cref="GameLaunchers"/>). Absichtlich Text statt
/// neuer <see cref="AppSource"/>-Werte: Ein älteres Panel kennt neue Enum-Werte nicht und würde die ganze Liste
/// ablehnen; ein unbekanntes Feld überliest es einfach.
/// </param>
/// <param name="LaunchUri">Start über den Launcher (z. B. com.epicgames.launcher://…), hat Vorrang vor der EXE.</param>
public sealed record AppEntry(
    string Id,
    string Name,
    AppSource Source,
    string? ExecutablePath = null,
    int? SteamAppId = null,
    long? SizeBytes = null,
    string? Publisher = null,
    string? Version = null,
    string? StoreAppUserModelId = null,
    bool IsSystemComponent = false,
    string? IconHash = null,
    string? CoverHash = null,
    string? LaunchArguments = null,
    string? Launcher = null,
    string? LaunchUri = null)
{
    /// <summary>Steam-Spiel oder Spiel aus einem anderen Launcher (Epic, GOG, …).</summary>
    [JsonIgnore]
    public bool IsGame => Source == AppSource.Steam || Launcher is not null;

    /// <summary>Startbefehl für den Launcher.</summary>
    public string? LaunchTarget => LaunchUri ?? Source switch
    {
        AppSource.Steam when SteamAppId is { } appId => $"steam://rungameid/{appId}",
        AppSource.StoreApp when StoreAppUserModelId is { } aumid => $@"shell:AppsFolder\{aumid}",
        _ => ExecutablePath,
    };
}

/// <summary>Alles, was der Agent auf dem PC gefunden hat.</summary>
/// <param name="ContentHash">Hash über die Einträge: gleich, wenn sich nichts geändert hat.</param>
public sealed record InventoryReport(DateTimeOffset CollectedAt, AppEntry[] Apps, string ContentHash);

/// <summary>Ein Bild zu einem App-Eintrag (Icon oder Cover), adressiert über seinen SHA-256-Hash.</summary>
public sealed record AppImage(string Hash, string ContentType, byte[] Data);
