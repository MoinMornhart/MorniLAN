using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Remote;

using NetworkInfo = Shared.Connection.NetworkInfo;

/// <summary>
/// Steuert den Fernzugriff auf dem verwalteten PC: Anfrage des Admins, Zustimmung des Freundes (oder sofort, wenn
/// erlaubt), Sunshine starten, Maus/Tastatur sperren. Der Zustand wird ans Panel gemeldet und (gekürzt) vom
/// Launcher angezeigt. Die Eingabesperre setzt der Launcher um (er läuft in der Sitzung des Freundes); hier wird
/// nur der Wunsch gehalten und gemeldet.
/// </summary>
/// <param name="ensureSunshine">Startet Sunshine und sagt, ob es läuft (injizierbar für Tests).</param>
/// <param name="sunshineInstalled">Ist Sunshine da? (injizierbar).</param>
/// <param name="hostAddress">Adresse für Moonlight (injizierbar).</param>
internal sealed class RemoteAccessService(
    ILogger<RemoteAccessService> logger,
    Func<bool>? ensureSunshine = null,
    Func<bool>? sunshineInstalled = null,
    Func<string?>? hostAddress = null,
    TimeProvider? time = null)
{
    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromSeconds(60);

    private readonly Func<bool> _ensureSunshine = ensureSunshine ?? SunshineControl.EnsureRunning;
    private readonly Func<bool> _sunshineInstalled = sunshineInstalled ?? SunshineControl.IsInstalled;
    private readonly Func<string?> _hostAddress = hostAddress ?? DefaultHost;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _lock = new();

    private RemoteSessionState _state = RemoteSessionState.Idle;
    private DateTimeOffset _consentDeadline;

    public event Action<RemoteSessionState>? StateChanged;

    public RemoteSessionState State
    {
        get
        {
            lock (_lock)
                return _state;
        }
    }

    /// <summary>Admin startet den Fernzugriff. Ohne Rückfrage sofort, sonst wird der Freund gefragt.</summary>
    public void Start(bool allowWithoutConsent)
    {
        if (!_sunshineInstalled())
        {
            Set(_state with
            {
                Phase = RemoteSessionPhase.Unavailable,
                Message = "Sunshine ist auf dem PC noch nicht eingerichtet. Bitte das Geräte-Setup mit Fernzugriff ausführen.",
                Since = _time.GetUtcNow(),
            });
            return;
        }
        if (allowWithoutConsent)
        {
            Activate();
        }
        else
        {
            lock (_lock)
                _consentDeadline = _time.GetUtcNow() + ConsentTimeout;
            Set(new RemoteSessionState(RemoteSessionPhase.WaitingForConsent, false, null,
                "Warte auf Bestätigung am PC …", _time.GetUtcNow()));
            logger.LogInformation("Fernzugriff angefragt, warte auf Bestätigung des Freundes");
        }
    }

    /// <summary>Antwort des Freundes (aus dem Launcher-Briefkasten).</summary>
    public void Consent(bool allow)
    {
        lock (_lock)
        {
            if (_state.Phase != RemoteSessionPhase.WaitingForConsent)
                return;
        }
        if (allow)
        {
            Activate();
        }
        else
        {
            Set(new RemoteSessionState(RemoteSessionPhase.Denied, false, null, "Der Freund hat abgelehnt.", _time.GetUtcNow()));
            logger.LogInformation("Fernzugriff vom Freund abgelehnt");
        }
    }

    /// <summary>Admin beendet die Sitzung (oder sie wird abgebrochen).</summary>
    public void Stop()
    {
        if (State.Phase == RemoteSessionPhase.Idle)
            return;
        Set(RemoteSessionState.Idle);
        logger.LogInformation("Fernzugriff beendet");
    }

    /// <summary>Maus/Tastatur des Freundes sperren oder freigeben (nur während einer laufenden Sitzung).</summary>
    public void SetInputLock(bool locked)
    {
        lock (_lock)
        {
            if (_state.Phase != RemoteSessionPhase.Active)
                return;
            _state = _state with { InputLocked = locked };
        }
        logger.LogInformation("Eingabesperre: {Locked}", locked);
        StateChanged?.Invoke(State);
    }

    /// <summary>Läuft die Zustimmungsfrist ab, wird die Anfrage verworfen. Regelmäßig aufrufen.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (_state.Phase != RemoteSessionPhase.WaitingForConsent || _time.GetUtcNow() < _consentDeadline)
                return;
        }
        Set(new RemoteSessionState(RemoteSessionPhase.Denied, false, null,
            "Keine Antwort am PC – Anfrage verworfen.", _time.GetUtcNow()));
    }

    private void Activate()
    {
        var running = _ensureSunshine();
        var host = _hostAddress();
        Set(new RemoteSessionState(RemoteSessionPhase.Active, false, running ? host : null,
            running ? "Fernzugriff läuft – mit Moonlight verbinden." : "Sunshine wird gestartet …", _time.GetUtcNow()));
        logger.LogInformation("Fernzugriff aktiv ({Host})", host ?? "Adresse folgt");
    }

    private void Set(RemoteSessionState state)
    {
        lock (_lock)
            _state = state;
        StateChanged?.Invoke(state);
    }

    private static string? DefaultHost()
    {
        // Eigene Adresse im Heimnetz bevorzugen (nicht die virtuellen Adapter)
        var addresses = NetworkInfo.LocalIPv4();
        var chosen = addresses.Where(a => !a.IsVirtual).Concat(addresses).FirstOrDefault();
        return chosen.Address is null ? null : $"{chosen.Address}:{SunshineControl.MoonlightPort}";
    }
}
