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
    Func<CancellationToken, Task<bool>>? installSunshine = null,
    Func<string, string, CancellationToken, Task<bool>>? acceptPin = null,
    TimeProvider? time = null)
{
    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromSeconds(60);

    private readonly Func<bool> _ensureSunshine = ensureSunshine ?? SunshineControl.EnsureRunning;
    private readonly Func<bool> _sunshineInstalled = sunshineInstalled ?? SunshineControl.IsInstalled;
    private readonly Func<string?> _hostAddress = hostAddress ?? DefaultHost;
    private readonly Func<CancellationToken, Task<bool>> _installSunshine = installSunshine ?? SunshineInstaller.InstallAsync;
    // (pin, Gerätename) → angenommen? Standard: Sunshines lokale API (ersetzt die Hand-Eingabe im Browser)
    private readonly Func<string, string, CancellationToken, Task<bool>> _acceptPin =
        acceptPin ?? SunshineConfigurator.Shared.AcceptPinAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _lock = new();

    private RemoteSessionState _state = RemoteSessionState.Idle;
    private DateTimeOffset _consentDeadline;
    private bool _installing;

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
            // Sunshine fehlt: automatisch per winget einrichten und danach den Fernzugriff fortsetzen (keine Handarbeit)
            if (!_installing)
                _ = InstallSunshineThenStartAsync(allowWithoutConsent);
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

    /// <summary>
    /// Der Admin hat die in Moonlight angezeigte PIN eingegeben. Der PC nimmt die Kopplung über Sunshines lokale API
    /// an – ganz ohne Web-Oberfläche. Läuft nebenher; das Ergebnis erscheint als Hinweis im Panel. Nur während einer
    /// laufenden Sitzung sinnvoll (Moonlight zeigt die PIN erst beim Verbinden).
    /// </summary>
    public void Pair(string pin)
    {
        lock (_lock)
        {
            if (_state.Phase != RemoteSessionPhase.Active)
                return;
        }
        _ = PairAsync(pin);
    }

    private async Task PairAsync(string pin)
    {
        bool accepted;
        try
        {
            accepted = await _acceptPin(pin, Environment.MachineName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Kopplung (PIN) fehlgeschlagen: {Error}", ex.Message);
            accepted = false;
        }

        RemoteSessionState? updated = null;
        lock (_lock)
        {
            if (_state.Phase == RemoteSessionPhase.Active)
                updated = _state = _state with
                {
                    Message = accepted
                        ? "Gekoppelt ✓ – Moonlight verbindet jetzt automatisch."
                        : "PIN stimmte nicht. In Moonlight eine neue PIN holen und erneut eingeben.",
                };
        }
        if (updated is not null)
        {
            logger.LogInformation("Kopplung per PIN {Result}", accepted ? "angenommen" : "abgelehnt");
            StateChanged?.Invoke(updated);
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

    /// <summary>
    /// Richtet Sunshine ein (winget) und startet danach den Fernzugriff wie gewünscht. Läuft nebenher; der Zustand
    /// „wird eingerichtet" wird gemeldet. Phase bleibt bewusst <see cref="RemoteSessionPhase.Unavailable"/>
    /// (kein neuer Enum-Wert, damit ein älteres Panel es versteht) – nur die Meldung sagt, was gerade passiert.
    /// </summary>
    private async Task InstallSunshineThenStartAsync(bool allowWithoutConsent)
    {
        lock (_lock)
        {
            if (_installing)
                return;
            _installing = true;
        }
        var ok = false;
        try
        {
            // Innerhalb des try, damit ein werfender StateChanged-Handler _installing nicht dauerhaft blockiert
            Set(new RemoteSessionState(RemoteSessionPhase.Unavailable, false, null,
                "Sunshine wird automatisch eingerichtet – das dauert ein paar Minuten …", _time.GetUtcNow()));
            logger.LogInformation("Sunshine fehlt – wird per winget eingerichtet");
            ok = await _installSunshine(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Sunshine konnte nicht eingerichtet werden: {Error}", ex.Message);
            ok = false;
        }
        finally
        {
            lock (_lock)
                _installing = false;
        }

        // Nach dem finally (also mit _installing == false), damit der fortgesetzte Start sauber durchläuft.
        // Alles gekapselt, weil diese Methode als fire-and-forget läuft und keine Exception verlieren soll.
        try
        {
            if (ok && _sunshineInstalled())
            {
                logger.LogInformation("Sunshine eingerichtet, Fernzugriff wird fortgesetzt");
                Start(allowWithoutConsent);
            }
            else
            {
                Set(new RemoteSessionState(RemoteSessionPhase.Unavailable, false, null,
                    "Sunshine ließ sich nicht automatisch einrichten. Bitte einmal von Hand installieren (winget: LizardByte.Sunshine).",
                    _time.GetUtcNow()));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Fernzugriff nach Sunshine-Einrichtung nicht fortgesetzt: {Error}", ex.Message);
        }
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
