namespace MorniLAN.Shared.Models;

/// <summary>Zustand einer Fernzugriffs-Sitzung auf dem verwalteten PC.</summary>
public enum RemoteSessionPhase
{
    /// <summary>Keine Sitzung.</summary>
    Idle,

    /// <summary>Der Admin hat angefragt, der PC fragt gerade den Freund.</summary>
    WaitingForConsent,

    /// <summary>Der Fernzugriff läuft (Sunshine ist bereit, der Admin kann mit Moonlight verbinden).</summary>
    Active,

    /// <summary>Der Freund hat abgelehnt (oder die Zeit lief ab).</summary>
    Denied,

    /// <summary>Sunshine ist auf dem PC nicht verfügbar – muss noch eingerichtet werden.</summary>
    Unavailable,
}

/// <summary>
/// Stand des Fernzugriffs, den der PC ans Panel und (gekürzt) an den Launcher meldet.
/// </summary>
/// <param name="Phase">Aktuelle Phase.</param>
/// <param name="InputLocked">Maus und Tastatur des Freundes sind gerade gesperrt.</param>
/// <param name="Host">Adresse für Moonlight (Host:Port), sobald die Sitzung aktiv ist.</param>
/// <param name="Message">Hinweis fürs Panel (z. B. warum Unavailable).</param>
/// <param name="Since">Seit wann die aktuelle Phase gilt.</param>
public sealed record RemoteSessionState(
    RemoteSessionPhase Phase,
    bool InputLocked,
    string? Host,
    string Message,
    DateTimeOffset Since)
{
    public static RemoteSessionState Idle { get; } = new(RemoteSessionPhase.Idle, false, null, "", DateTimeOffset.UnixEpoch);

    /// <summary>Läuft gerade eine Sitzung oder wird gefragt? (Für den sichtbaren Hinweis im Launcher.)</summary>
    public bool IsVisibleToUser => Phase is RemoteSessionPhase.WaitingForConsent or RemoteSessionPhase.Active;
}

/// <summary>Einstellungen des Fernzugriffs je PC, die das Panel verwaltet.</summary>
/// <param name="AllowWithoutConsent">
/// „Ohne Rückfrage erlauben“: Dann startet der Fernzugriff sofort. Sonst fragt der PC immer erst den Freund.
/// Standard false (fragen), damit nichts ungefragt passiert.
/// </param>
public sealed record RemoteAccessSettings(bool AllowWithoutConsent = false);
