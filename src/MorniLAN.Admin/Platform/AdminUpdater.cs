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
    /// Lädt das Admin-Setup, prüft die Prüfsumme und startet es,
    /// sobald das Panel beendet ist. Danach muss der Aufrufer die App beenden.
    /// </summary>
    public async Task<bool> PrepareAndLaunchAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(AdminPaths.Data, "updates");
        var setup = await GitHubReleases.DownloadAsync(update, folder, cancellationToken);
        var log = Path.Combine(AdminPaths.Logs, $"update-{update.Version}.log");
        var script = LaunchScript(Environment.ProcessId, setup, log);
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        var process = Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Log.Information("Update auf v{Version} gestartet ({Setup})", update.Version, setup);
        return process is not null;
    }

    /// <summary>
    /// Wartet, bis das Panel wirklich beendet ist (der Server-Stopp dauert bis zu 5 s), und startet erst dann
    /// das stille Setup. Mit fester Wartezeit brach das Setup ab, weil die Dateien noch belegt waren.
    /// Nach 60 s geht es trotzdem los; dann schließt das Setup das Panel selbst (CloseApplications=force).
    /// </summary>
    internal static string LaunchScript(int panelProcessId, string setup, string log) =>
        $"Wait-Process -Id {panelProcessId} -Timeout 60 -ErrorAction SilentlyContinue; " +
        $"Start-Process -FilePath {Quote(setup)} -ArgumentList {Quote($"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{log}\"")}";

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";
}
