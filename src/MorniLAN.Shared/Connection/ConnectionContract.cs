using MorniLAN.Shared.Models;

namespace MorniLAN.Shared.Connection;

/// <summary>Feste Werte der Verbindung Agent ↔ Admin-Panel.</summary>
public static class ConnectionDefaults
{
    /// <summary>Pfad des SignalR-Hubs im Admin-Panel.</summary>
    public const string HubPath = "/hub/agent";

    /// <summary>So oft meldet der Agent seinen Status.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>Ohne Heartbeat in dieser Zeit gilt ein PC als offline.</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(45);

    /// <summary>So oft sendet das Admin-Panel seinen UDP-Beacon ins LAN.</summary>
    public static readonly TimeSpan BeaconInterval = TimeSpan.FromSeconds(3);

    /// <summary>Fehlversuche bei der Code-Eingabe, danach wird die Pairing-Anfrage verworfen.</summary>
    public const int MaxPairingAttempts = 5;

    /// <summary>So oft liest der Agent die Programmliste neu ein (geschickt wird nur bei Änderungen).</summary>
    public static readonly TimeSpan InventoryInterval = TimeSpan.FromMinutes(15);

    /// <summary>Größte erlaubte SignalR-Nachricht (Programmliste, einzelnes Bild).</summary>
    public const long MaxMessageBytes = 2 * 1024 * 1024;

    /// <summary>Größtes erlaubtes Bild (Cover sind auf 300×450 verkleinert, das reicht großzügig).</summary>
    public const int MaxImageBytes = 512 * 1024;
}

/// <summary>Methoden, die der Agent im Admin-Panel aufruft (SignalR-Hub).</summary>
public interface IAdminHub
{
    /// <summary>Erster Aufruf nach jedem Verbindungsaufbau. Das Panel prüft das Client-Zertifikat.</summary>
    Task<HelloResponse> Hello(HelloRequest request);

    /// <summary>Nicht gekoppelter Agent bittet um Pairing (Beweis für den Code, den der Agent anzeigt).</summary>
    Task RequestPairing(PairingRequest request);

    /// <summary>Agent hat den Beweis des Panels geprüft, erst jetzt speichert das Panel das Gerät.</summary>
    Task<HelloResponse> ConfirmPairing();

    /// <summary>Regelmäßige Statusmeldung.</summary>
    Task Heartbeat(DeviceStatus status);

    /// <summary>Alle gefundenen Programme und Spiele. Antwort: Hashes der Bilder, die dem Panel noch fehlen.</summary>
    Task<string[]> ReportInventory(InventoryReport report);

    /// <summary>Ein Bild nachliefern, das das Panel angefordert hat.</summary>
    Task UploadImage(AppImage image);

    /// <summary>Stand des automatischen Updates (prüft, lädt, wartet auf Spielende, …).</summary>
    Task ReportUpdateState(Updates.AgentUpdateState state);
}

/// <summary>Methoden, die das Admin-Panel beim Agent aufruft.</summary>
public interface IAgentClient
{
    /// <summary>Code war richtig, hier ist der Gegenbeweis des Panels.</summary>
    Task OnPairingApproved(PairingApproval approval);

    /// <summary>Pairing abgelehnt (vom Admin oder nach zu vielen Fehlversuchen).</summary>
    Task OnPairingRejected(string reason);

    /// <summary>Der Admin hat den PC entfernt.</summary>
    Task OnUnpaired();

    /// <summary>Der Admin möchte die Programmliste jetzt neu einlesen lassen.</summary>
    Task OnRefreshInventory();

    /// <summary>Der Admin möchte das Update jetzt (statt erst beim nächsten Prüfen).</summary>
    Task OnInstallUpdate();
}

public enum HelloStatus
{
    /// <summary>Zertifikat ist bekannt, Heartbeats sind erlaubt.</summary>
    Paired,

    /// <summary>Unbekannt, der Agent muss erst koppeln.</summary>
    PairingRequired,
}

public sealed record HelloRequest(DeviceInfo Device);

/// <param name="AdminEndpoints">Adressen, unter denen das Panel erreichbar ist (LAN, Tailscale).</param>
public sealed record HelloResponse(HelloStatus Status, string AdminName, string[] AdminEndpoints);

/// <param name="AgentProof">Base64, siehe <see cref="PairingProof"/>.</param>
public sealed record PairingRequest(DeviceInfo Device, string AgentProof);

/// <param name="AdminProof">Base64, siehe <see cref="PairingProof"/>.</param>
public sealed record PairingApproval(string AdminProof);
