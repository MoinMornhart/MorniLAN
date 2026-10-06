using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using MorniLAN.Shared;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Agent.Connection;

/// <summary>
/// Schickt dem Launcher eine Zeile JSON über eine Named Pipe: seinen Status (<see cref="LocalStatusPipe"/>) bzw. die
/// freigegebenen Apps (<see cref="LauncherAppsPipe"/>). Angemeldete Benutzer dürfen nur lesen: kein Schreiben,
/// keine eigene Pipe-Instanz (gegen Pipe-Squatting).
/// </summary>
/// <param name="payload">Liefert die Zeile, die jeder Client bekommt.</param>
/// <param name="grantCurrentUser">
/// Vollzugriff für das eigene Konto. Im Dienst ist das SYSTEM; im Test aus, damit der Client (gleiches Konto)
/// wie ein Standardbenutzer nur die Rechte der Gruppe "Authentifizierte Benutzer" bekommt.
/// </param>
internal sealed class LocalStatusServer(Func<string> payload, ILogger<LocalStatusServer> logger,
    string pipeName = MorniLanConstants.LauncherPipeName, bool grantCurrentUser = true) : BackgroundService
{
    // Die App-Liste mit Bildern kann einige MB groß sein
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Der ganze Durchlauf in try/catch: KEIN einzelner Pipe-Fehler darf den Kanal dauerhaft töten,
            // sonst bekommt der Launcher nie wieder eine Antwort (bis der Dienst neu startet).
            try
            {
                var pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                    0, 64 * 1024, CreateSecurity(grantCurrentUser));
                try
                {
                    await pipe.WaitForConnectionAsync(stoppingToken);
                }
                catch
                {
                    await pipe.DisposeAsync();
                    throw;
                }
                _ = AnswerAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Kanal {Pipe} für den Launcher gestört ({Error}), neuer Versuch gleich", pipeName, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(WriteTimeout);
            try
            {
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(payload().AsMemory(), timeout.Token);
                pipe.WaitForPipeDrain();
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                // Launcher hat schon aufgelegt, die nächste Abfrage kommt in zwei Sekunden.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Antwort auf {Pipe} fehlgeschlagen", pipeName);
            }
        }
    }

    internal static PipeSecurity CreateSecurity(bool grantCurrentUser)
    {
        var security = new PipeSecurity();
        // Genau das, was ein nur lesender Client verlangt: GENERIC_READ plus FILE_WRITE_ATTRIBUTES
        // (damit stellt .NET den Lesemodus ein). Kein WriteData, kein CreateNewInstance.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.Read | PipeAccessRights.WriteAttributes | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        using var current = WindowsIdentity.GetCurrent();
        if (current.User is { } self)
        {
            // Im Test nur, was der Server zum Anlegen weiterer Instanzen braucht, aber kein Schreibrecht
            // auf die Daten: So bekommt ein Client desselben Kontos keine Rechte über die eines Standardbenutzers.
            security.AddAccessRule(new PipeAccessRule(self,
                grantCurrentUser
                    ? PipeAccessRights.FullControl
                    : PipeAccessRights.CreateNewInstance | PipeAccessRights.ReadPermissions
                      | PipeAccessRights.ChangePermissions | PipeAccessRights.TakeOwnership,
                AccessControlType.Allow));
        }
        return security;
    }
}
