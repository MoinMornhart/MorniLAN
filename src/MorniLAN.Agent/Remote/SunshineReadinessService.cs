using Microsoft.Extensions.Hosting.WindowsServices;
using MorniLAN.Agent.Platform;

namespace MorniLAN.Agent.Remote;

/// <summary>
/// Lädt Sunshine (den Fernzugriffs-Host) automatisch im Hintergrund herunter und richtet es ein, sobald das Gerät
/// gekoppelt ist – damit der Fernzugriff ohne Handarbeit und ohne Verzögerung beim ersten Zugriff bereitsteht.
/// Nie während eines Spiels, und nur als Dienst (nicht in der Entwickler-Konsole). Hat es einmal geklappt, ist Ruhe.
/// </summary>
internal sealed class SunshineReadinessService(
    ILogger<SunshineReadinessService> logger,
    Func<bool> isPaired,
    Func<bool>? installed = null,
    Func<bool>? isGameRunning = null,
    Func<CancellationToken, Task<bool>>? install = null,
    bool? canInstall = null,
    TimeProvider? time = null) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(30);

    private readonly Func<bool> _installed = installed ?? SunshineControl.IsInstalled;
    private readonly Func<bool> _isGameRunning = isGameRunning ?? SteamActivity.IsGameRunning;
    private readonly Func<CancellationToken, Task<bool>> _install = install ?? SunshineInstaller.InstallAsync;
    private readonly bool _canInstall = canInstall ?? WindowsServiceHelpers.IsWindowsService();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_canInstall)
            return; // In der Dev-Konsole nichts installieren
        try
        {
            await Task.Delay(InitialDelay, _time, stoppingToken);
            if (await TryEnsureAsync(stoppingToken))
                return;
            using var timer = new PeriodicTimer(Retry, _time);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                if (await TryEnsureAsync(stoppingToken))
                    return;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Ein Versuch. true = fertig (installiert oder war schon da), nicht mehr nötig.</summary>
    internal async Task<bool> TryEnsureAsync(CancellationToken cancellationToken)
    {
        if (_installed())
            return true;
        if (!isPaired())
            return false; // Erst wenn gekoppelt – ohne Admin macht Fernzugriff keinen Sinn
        if (_isGameRunning())
            return false; // nicht mitten im Spiel

        logger.LogInformation("Sunshine (Fernzugriff) wird automatisch heruntergeladen und eingerichtet …");
        try
        {
            if (await _install(cancellationToken) && _installed())
            {
                logger.LogInformation("Sunshine ist eingerichtet – der Fernzugriff ist bereit.");
                return true;
            }
            logger.LogWarning("Sunshine konnte noch nicht eingerichtet werden, neuer Versuch später.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Sunshine-Einrichtung fehlgeschlagen: {Error}", ex.Message);
        }
        return false;
    }
}
