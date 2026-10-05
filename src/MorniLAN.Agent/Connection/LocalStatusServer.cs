using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using MorniLAN.Shared;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Agent.Connection;

/// <summary>
/// Schickt dem Launcher seinen Status über eine Named Pipe (siehe <see cref="LocalStatusPipe"/>).
/// Angemeldete Benutzer dürfen nur lesen: kein Schreiben, keine eigene Pipe-Instanz (gegen Pipe-Squatting).
/// </summary>
/// <param name="grantCurrentUser">
/// Vollzugriff für das eigene Konto. Im Dienst ist das SYSTEM; im Test aus, damit der Client (gleiches Konto)
/// wie ein Standardbenutzer nur die Rechte der Gruppe "Authentifizierte Benutzer" bekommt.
/// </param>
internal sealed class LocalStatusServer(AdminConnectionService connection, ILogger<LocalStatusServer> logger,
    string pipeName = MorniLanConstants.LauncherPipeName, bool grantCurrentUser = true) : BackgroundService
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                    0, 4096, CreateSecurity(grantCurrentUser));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Statuskanal für den Launcher nicht verfügbar ({Error}), neuer Versuch in 30 s", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                return;
            }
            _ = AnswerAsync(pipe, stoppingToken);
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
                await writer.WriteLineAsync(LocalStatusPipe.Serialize(connection.LocalStatus()).AsMemory(), timeout.Token);
                pipe.WaitForPipeDrain();
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                // Launcher hat schon aufgelegt, die nächste Abfrage kommt in zwei Sekunden.
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
