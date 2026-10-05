using System.Diagnostics;
using MorniLAN.Admin.Server;
using MorniLAN.Shared;
using MorniLAN.Shared.Updates;
using Serilog;

namespace MorniLAN.Admin.Platform;

/// <summary>
/// Updates für das Admin-Panel selbst und Auskunft über die neueste Geräte-Version (für die Anzeige je PC).
/// Das Admin-Setup läuft pro Benutzer, braucht also keine Admin-Rechte; es startet das Panel danach neu.
/// </summary>
internal sealed class AdminUpdater
{
    public const bool IncludePrereleases = true; // vom Nutzer so gewünscht: Betas immer mit anbieten

    public AvailableUpdate? AdminUpdate { get; private set; }
    public AvailableUpdate? NewestDeviceRelease { get; private set; }
    public DateTimeOffset? LastCheck { get; private set; }

    public static SemanticVersion CurrentVersion => SemanticVersion.Parse(VersionInfo.Version);

    /// <summary>Fragt GitHub. Wirft bei Netzfehlern, der Aufrufer zeigt die Meldung an.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        var releases = await GitHubReleases.FetchAsync(cancellationToken);
        AdminUpdate = GitHubReleases.FindUpdate(releases, CurrentVersion, UpdateProduct.Admin, IncludePrereleases);
        // Neueste Geräte-Version überhaupt (Vergleich je PC macht die Oberfläche)
        NewestDeviceRelease = GitHubReleases.FindUpdate(releases, SemanticVersion.Parse("0.0.0"), UpdateProduct.Device,
            IncludePrereleases);
        LastCheck = DateTimeOffset.Now;
        Log.Information("Update-Prüfung: Panel {Admin}, Geräte-App neueste {Device}",
            AdminUpdate?.Version.ToString() ?? "aktuell", NewestDeviceRelease?.Version.ToString() ?? "–");
    }

    /// <summary>Ist für einen PC mit dieser Agent-Version ein Update da?</summary>
    public string? DeviceUpdateFor(string? agentVersion) =>
        NewestDeviceRelease is { } newest && SemanticVersion.TryParse(agentVersion, out var current) && newest.Version > current
            ? newest.Version.ToString()
            : null;

    /// <summary>
    /// Lädt das Admin-Setup, prüft die Prüfsumme und startet es mit kurzer Verzögerung, damit das Panel
    /// sich vorher sauber beenden kann. Danach muss der Aufrufer die App beenden.
    /// </summary>
    public async Task<bool> PrepareAndLaunchAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(AdminPaths.Data, "updates");
        var setup = await GitHubReleases.DownloadAsync(update, folder, cancellationToken);
        var log = Path.Combine(AdminPaths.Logs, $"update-{update.Version}.log");
        // ping als Wartezeit: das Panel hat drei Sekunden, um sich zu beenden, bevor das Setup Dateien ersetzt
        var command = $"/c ping 127.0.0.1 -n 4 >nul & \"{setup}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{log}\"";
        var process = Process.Start(new ProcessStartInfo("cmd.exe", command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Log.Information("Update auf v{Version} gestartet ({Setup})", update.Version, setup);
        return process is not null;
    }
}
