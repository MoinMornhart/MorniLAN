using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Shared.Connection;

public enum AgentLinkState
{
    /// <summary>Kein Admin-Panel bekannt oder erreichbar.</summary>
    Searching,

    /// <summary>Verbunden, wartet darauf, dass der Admin den Pairing-Code eingibt.</summary>
    WaitingForPairing,

    /// <summary>Gekoppelt und online, Heartbeats laufen.</summary>
    Online,
}

/// <summary>Was der Agent dem Launcher (und später dem Einrichtungsassistenten) über sich meldet.</summary>
/// <param name="PairingCode">Anzeigeform "XXXX-XXXX", nur solange nicht gekoppelt.</param>
public sealed record AgentLocalStatus(
    AgentLinkState State,
    string? PairingCode,
    string? AdminName,
    string MachineName,
    string AgentVersion,
    DateTimeOffset Timestamp);

/// <summary>
/// Lokaler Statuskanal Agent → Launcher über die Named Pipe <see cref="MorniLanConstants.LauncherPipeName"/>.
/// Protokoll: Der Launcher verbindet sich NUR LESEND, der Agent schreibt sofort eine Zeile JSON und legt auf.
/// Bewusst ohne Anfrage: Wer schreibend öffnet, verlangt unter Windows auch FILE_APPEND_DATA, und das ist bei
/// Pipes das Recht, eigene Instanzen anzulegen. Das bekommen Standardbenutzer nicht (gegen Pipe-Squatting).
/// </summary>
public static class LocalStatusPipe
{
    /// <summary>Fragt den Agent ab. null, wenn er nicht läuft oder nicht antwortet.</summary>
    public static async Task<AgentLocalStatus?> QueryAsync(TimeSpan timeout, CancellationToken cancellationToken = default,
        string pipeName = MorniLanConstants.LauncherPipeName)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cts.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync(cts.Token);
            return line is null ? null : JsonSerializer.Deserialize(line, MorniLanJsonContext.Default.AgentLocalStatus);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException or JsonException
                                       or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string Serialize(AgentLocalStatus status) =>
        JsonSerializer.Serialize(status, MorniLanJsonContext.Default.AgentLocalStatus);
}
