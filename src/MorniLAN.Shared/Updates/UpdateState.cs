namespace MorniLAN.Shared.Updates;

public enum UpdatePhase
{
    /// <summary>Noch nicht geprüft.</summary>
    Unknown,
    UpToDate,
    Checking,
    /// <summary>Neue Version gefunden, Installation folgt (oder ist im Konsolenmodus abgeschaltet).</summary>
    Available,
    /// <summary>Ein Spiel läuft, das Update wartet.</summary>
    WaitingForGame,
    Downloading,
    Installing,
    Failed,
}

/// <summary>Was der Agent über sein Update meldet (für die Anzeige im Panel).</summary>
public sealed record AgentUpdateState(UpdatePhase Phase, string Message, string? AvailableVersion, DateTimeOffset Timestamp);
