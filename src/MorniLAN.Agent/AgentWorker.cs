using MorniLAN.Agent.Platform;
using MorniLAN.Shared;

namespace MorniLAN.Agent;

/// <summary>Meldet beim Start Version und Windows-Edition. Die Verbindung hält der AdminConnectionService.</summary>
internal sealed class AgentWorker(ILogger<AgentWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var edition = WindowsEditionDetector.Detect();
        logger.LogInformation("{Product} Agent {Version} gestartet auf {Machine}", MorniLanConstants.ProductName,
            VersionInfo.Display, Environment.MachineName);
        logger.LogInformation(
            "Windows: {Edition} (EditionID {EditionId}, Build {Build}) → Sperre per {Lockdown}, RDP-Host: {Rdp}",
            edition.FriendlyName, edition.EditionId, edition.Build, edition.PreferredLockdown, edition.SupportsRdpHost);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Agent wird beendet");
        await base.StopAsync(cancellationToken);
    }
}
