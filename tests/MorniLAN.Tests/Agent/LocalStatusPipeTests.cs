using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MorniLAN.Agent.Connection;
using MorniLAN.Agent.Platform;
using MorniLAN.Shared.Connection;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Agent;

/// <summary>Sammelt Log-Meldungen, damit ein fehlgeschlagener Test zeigt, was der Server gemeldet hat.</summary>
internal sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Enqueue($"{logLevel}: {formatter(state, exception)}");
}

/// <summary>
/// Im echten Betrieb läuft der Server als SYSTEM und der Launcher als Standardbenutzer. Ein Test läuft unter
/// einem Konto; mit <c>grantCurrentUser: false</c> bekommt der Client nur die Rechte eines Standardbenutzers.
/// Weitere Pipe-Instanzen kann der Server dann aber nicht anlegen (dafür verlangt Windows Schreibrechte),
/// deshalb prüft jeder Test mit eingeschränkten Rechten genau eine Verbindung.
/// </summary>
public sealed class LocalStatusPipeTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LocalStatusServer CreateServer(string pipeName, bool grantCurrentUser, ListLogger<LocalStatusServer> log)
    {
        var options = Options.Create(new AgentConnectionOptions { DataDirectory = _dir.Path, EnableDiscovery = false });
        var connection = new AdminConnectionService(options, new AgentStateStore(_dir.Path),
            AgentIdentity.LoadOrCreate(_dir.Path, NullLogger.Instance), new SystemStatusCollector(),
            NullLogger<AdminConnectionService>.Instance);
        return new LocalStatusServer(() => LocalStatusPipe.Serialize(connection.LocalStatus()), log, pipeName, grantCurrentUser);
    }

    private static string NewPipeName() => "MorniLAN.Test." + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task StandardUser_CanReadStatus()
    {
        var pipeName = NewPipeName();
        var log = new ListLogger<LocalStatusServer>();
        using var server = CreateServer(pipeName, grantCurrentUser: false, log);
        await server.StartAsync(Ct);

        var status = await LocalStatusPipe.QueryAsync(TimeSpan.FromSeconds(5), Ct, pipeName);

        Assert.True(status is not null, $"Keine Antwort. Server-Log: {string.Join(" | ", log.Messages)}");
        Assert.Equal(AgentLinkState.Searching, status.State);
        Assert.Null(status.PairingCode);
        Assert.Equal(Environment.MachineName, status.MachineName);
        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StandardUser_CannotOpenPipeForWriting()
    {
        // Genau der Fehler aus dem Test auf MORNI: schreibend öffnen verlangt mehr Rechte, als Standardbenutzer haben.
        var pipeName = NewPipeName();
        using var server = CreateServer(pipeName, grantCurrentUser: false, new ListLogger<LocalStatusServer>());
        await server.StartAsync(Ct);

        await using var writer = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        Assert.Throws<UnauthorizedAccessException>(() => writer.Connect(2000));
        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Server_AnswersManyQueriesInARow()
    {
        var pipeName = NewPipeName();
        var log = new ListLogger<LocalStatusServer>();
        using var server = CreateServer(pipeName, grantCurrentUser: true, log);
        await server.StartAsync(Ct);

        for (var i = 0; i < 5; i++)
        {
            var status = await LocalStatusPipe.QueryAsync(TimeSpan.FromSeconds(5), Ct, pipeName);
            Assert.True(status is not null, $"Abfrage {i + 1} ohne Antwort. Server-Log: {string.Join(" | ", log.Messages)}");
        }
        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Acl_AuthenticatedUsers_MayOnlyRead()
    {
        var security = LocalStatusServer.CreateSecurity(grantCurrentUser: true);
        var authenticated = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var rule = Assert.Single(security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>(),
            r => r.IdentityReference.Equals(authenticated));

        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.True(rule.PipeAccessRights.HasFlag(PipeAccessRights.ReadData));
        Assert.False(rule.PipeAccessRights.HasFlag(PipeAccessRights.WriteData), "Standardbenutzer dürfen nicht schreiben");
        Assert.False(rule.PipeAccessRights.HasFlag(PipeAccessRights.CreateNewInstance),
            "Standardbenutzer dürfen keine eigene Pipe-Instanz anlegen (Pipe-Squatting)");
        Assert.False(rule.PipeAccessRights.HasFlag(PipeAccessRights.ChangePermissions));
    }

    [Fact]
    public async Task Query_ReturnsNull_WhenAgentIsNotRunning()
    {
        var status = await LocalStatusPipe.QueryAsync(TimeSpan.FromMilliseconds(300), Ct, "MorniLAN.Test.Gibtsnicht." + Guid.NewGuid().ToString("N"));
        Assert.Null(status);
    }
}
