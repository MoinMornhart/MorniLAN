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
    private readonly SemaphoreSlim _trigger = new(0, 1);
    private AgentUpdateState _state = new(UpdatePhase.Unknown, "Noch nicht geprüft", null, DateTimeOffset.UtcNow);

    public AgentUpdateService(ILogger<AgentUpdateService> logger)
        : this(logger, GitHubReleases.FetchAsync, SteamActivity.IsGameRunning, WindowsServiceHelpers.IsWindowsService(),
            (update, ct) => GitHubReleases.DownloadAsync(update, Path.Combine(AgentPaths.Data, "updates"), ct),
            (setup, log) => GitHubReleases.StartSilentInstall(setup, log))
    {
    }

    internal AgentUpdateService(ILogger<AgentUpdateService> logger, Func<CancellationToken, Task<IReadOnlyList<ReleaseInfo>>> fetch,
        Func<bool> isGameRunning, bool canInstall, Func<AvailableUpdate, CancellationToken, Task<string>> download,
        Action<string, string> launch)
    {
        _download = download;
        _launch = launch;
        _logger = logger;
        _fetch = fetch;
        _isGameRunning = isGameRunning;
        _canInstall = canInstall;
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
        var wait = InitialDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _trigger.WaitAsync(wait, stoppingToken); }
            catch (OperationCanceledException) { return; }

            wait = await CheckAndInstallAsync(stoppingToken) ? GameRetry : CheckInterval;
        }
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
