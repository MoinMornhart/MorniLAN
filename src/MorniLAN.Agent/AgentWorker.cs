using MorniLAN.Agent.Platform;
using MorniLAN.Shared;

namespace MorniLAN.Agent;

/// <summary>
/// Hauptschleife des Agents. In Meilenstein 1 nur Start-Info und ein lokaler Heartbeat;
/// Verbindung zum Admin-Panel folgt in Meilenstein 2.
/// </summary>
internal sealed class AgentWorker(ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var edition = WindowsEditionDetector.Detect();
        logger.LogInformation("{Product} Agent {Version} gestartet auf {Machine}", MorniLanConstants.ProductName,
            VersionInfo.Display, Environment.MachineName);
        logger.LogInformation(
            "Windows: {Edition} (EditionID {EditionId}, Build {Build}) → Sperre per {Lockdown}, RDP-Host: {Rdp}",
            edition.FriendlyName, edition.EditionId, edition.Build, edition.PreferredLockdown, edition.SupportsRdpHost);

        using var timer = new PeriodicTimer(HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                logger.LogDebug("Heartbeat");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normales Beenden
        }

        logger.LogInformation("Agent wird beendet");
    }
}
