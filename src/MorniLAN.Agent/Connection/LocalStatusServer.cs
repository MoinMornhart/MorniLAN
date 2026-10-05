using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using MorniLAN.Shared;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Agent.Connection;

/// <summary>
/// Beantwortet Statusanfragen des Launchers über eine Named Pipe (siehe <see cref="LocalStatusPipe"/>).
/// Jeder angemeldete Benutzer darf lesen, aber keine eigene Pipe-Instanz anlegen (gegen Pipe-Squatting).
/// </summary>
internal sealed class LocalStatusServer(AdminConnectionService connection, ILogger<LocalStatusServer> logger,
    string pipeName = MorniLanConstants.LauncherPipeName) : BackgroundService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                    4096, 4096, CreateSecurity());
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
            timeout.CancelAfter(RequestTimeout);
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var request = await reader.ReadLineAsync(timeout.Token);
                if (request == LocalStatusPipe.StatusRequest)
                    await writer.WriteLineAsync(LocalStatusPipe.Serialize(connection.LocalStatus()).AsMemory(), timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                // Launcher hat aufgelegt oder trödelt, nächste Anfrage kommt bestimmt.
            }
        }
    }

    private static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes
            | PipeAccessRights.Synchronize, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        using var current = WindowsIdentity.GetCurrent();
        if (current.User is { } self)
            security.AddAccessRule(new PipeAccessRule(self, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
