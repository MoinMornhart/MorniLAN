using Microsoft.Extensions.Hosting.WindowsServices;
using MorniLAN.Agent.Platform;
using MorniLAN.Shared;
using MorniLAN.Shared.Updates;

namespace MorniLAN.Agent.Updates;

/// <summary>
/// Hält den Agent (und mit ihm den Launcher) aktuell: prüft GitHub zwei Minuten nach dem Start und dann alle
/// sechs Stunden, auf Wunsch des Admins sofort. Installiert wird nie, solange ein Steam-Spiel läuft.
/// Das Setup beendet den Dienst, ersetzt die Dateien und startet ihn neu.
/// Im Konsolenmodus (Entwicklung) wird nur geprüft, nie installiert.
/// </summary>
internal sealed class AgentUpdateService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan GameRetry = TimeSpan.FromMinutes(10);

    private readonly ILogger<AgentUpdateService> _logger;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ReleaseInfo>>> _fetch;
    private readonly Func<bool> _isGameRunning;
    private readonly bool _canInstall;
    private readonly Func<AvailableUpdate, CancellationToken, Task<string>> _download;
    private readonly Action<string, string> _launch;
    private readonly string _dataDir;
    private readonly Func<string> _runningVersion;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _trigger = new(0, 1);
    private AgentUpdateState _state = new(UpdatePhase.Unknown, "Noch nicht geprüft", null, DateTimeOffset.UtcNow);

    public AgentUpdateService(ILogger<AgentUpdateService> logger)
        : this(logger, ct => GitHubReleases.FetchAsync(ct, Path.Combine(AgentPaths.Data, "updates")),
            SteamActivity.IsGameRunning, WindowsServiceHelpers.IsWindowsService(),
            (update, ct) => GitHubReleases.DownloadAsync(update, Path.Combine(AgentPaths.Data, "updates"), ct),
            (setup, log) => GitHubReleases.StartSilentInstall(setup, log), AgentPaths.Data)
    {
    }

    internal AgentUpdateService(ILogger<AgentUpdateService> logger, Func<CancellationToken, Task<IReadOnlyList<ReleaseInfo>>> fetch,
        Func<bool> isGameRunning, bool canInstall, Func<AvailableUpdate, CancellationToken, Task<string>> download,
        Action<string, string> launch, string dataDir, Func<string>? runningVersion = null, TimeProvider? time = null)
    {
        _download = download;
        _launch = launch;
        _logger = logger;
        _fetch = fetch;
        _isGameRunning = isGameRunning;
        _canInstall = canInstall;
        _dataDir = dataDir;
        _runningVersion = runningVersion ?? (() => VersionInfo.Version);
        _time = time ?? TimeProvider.System;
    }

    public AgentUpdateState State => Volatile.Read(ref _state);

    public event Action<AgentUpdateState>? StateChanged;

    /// <summary>Vom Admin angestoßen: sofort prüfen und (wenn kein Spiel läuft) installieren.</summary>
    public void RequestNow()
    {
        if (_trigger.CurrentCount == 0)
            _trigger.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        VerifyPreviousUpdate();
        var wait = InitialDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _trigger.WaitAsync(wait, stoppingToken); }
            catch (OperationCanceledException) { return; }

            wait = await CheckAndInstallAsync(stoppingToken) ? GameRetry : CheckInterval;
        }
    }

    /// <summary>
    /// Prüft nach einem Neustart, ob das zuletzt gestartete Update wirklich angekommen ist (Marker vom letzten Mal).
    /// Läuft jetzt die Zielversion → Erfolg. Läuft noch die alte → Update nicht durchgekommen, melden und beim
    /// nächsten Durchlauf erneut versuchen. In jedem Fall wird der Marker danach entfernt.
    /// </summary>
    internal void VerifyPreviousUpdate()
    {
        if (UpdateMarker.Read(_dataDir) is not { } pending)
            return;
        var current = _runningVersion();
        if (string.Equals(current, pending.TargetVersion, StringComparison.OrdinalIgnoreCase))
            Set(UpdatePhase.UpToDate, $"✓ auf v{current} aktualisiert");
        else
            Set(UpdatePhase.Failed,
                $"Update auf v{pending.TargetVersion} hat nicht gegriffen – weiter auf v{current}, neuer Versuch folgt",
                pending.TargetVersion);
        UpdateMarker.Clear(_dataDir);
    }

    /// <summary>Ein Durchlauf. true = später erneut versuchen (Spiel läuft).</summary>
    internal async Task<bool> CheckAndInstallAsync(CancellationToken cancellationToken)
    {
        try
        {
            Set(UpdatePhase.Checking, "Suche nach Updates …");
            var current = SemanticVersion.Parse(VersionInfo.Version);
            var update = GitHubReleases.FindUpdate(await _fetch(cancellationToken), current, UpdateProduct.Device,
                includePrereleases: true);
            if (update is null)
            {
                Set(UpdatePhase.UpToDate, $"Aktuell (v{current})");
                return false;
            }

            var version = update.Version.ToString();
            if (!_canInstall)
            {
                Set(UpdatePhase.Available, $"v{version} verfügbar (Konsolenmodus: bitte das Setup verwenden)", version);
                return false;
            }
            if (_isGameRunning())
            {
                Set(UpdatePhase.WaitingForGame, $"v{version} wartet, bis das Spiel beendet ist", version);
                return true;
            }

            Set(UpdatePhase.Downloading, $"Lade v{version} …", version);
            var setup = await _download(update, cancellationToken);

            if (_isGameRunning())
            {
                Set(UpdatePhase.WaitingForGame, $"v{version} wartet, bis das Spiel beendet ist", version);
                return true;
            }
            Set(UpdatePhase.Installing, $"Installiere v{version}, der Dienst startet gleich neu", version);
            _logger.LogInformation("Starte Update auf v{Version}: {Setup}", version, setup);
            // Marker, damit der neu gestartete Dienst prüfen kann, ob das Update wirklich griff
            UpdateMarker.Write(_dataDir, version, _time.GetUtcNow());
            _launch(setup, Path.Combine(AgentPaths.Logs, $"update-{version}.log"));
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Update fehlgeschlagen: {Error}", ex.Message);
            Set(UpdatePhase.Failed, $"Update fehlgeschlagen: {ex.Message}", State.AvailableVersion);
            return false;
        }
    }

    private void Set(UpdatePhase phase, string message, string? version = null)
    {
        var state = new AgentUpdateState(phase, message, version, DateTimeOffset.UtcNow);
        Volatile.Write(ref _state, state);
        if (phase is not UpdatePhase.Checking)
            _logger.LogInformation("Update: {Message}", message);
        StateChanged?.Invoke(state);
    }
}
