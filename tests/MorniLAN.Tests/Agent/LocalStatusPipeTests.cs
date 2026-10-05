using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MorniLAN.Agent.Connection;
using MorniLAN.Agent.Platform;
using MorniLAN.Shared.Connection;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Agent;

public sealed class LocalStatusPipeTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Launcher_ReadsAgentStatus_OverNamedPipe()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipeName = "MorniLAN.Test." + Guid.NewGuid().ToString("N");
        var options = Options.Create(new AgentConnectionOptions { DataDirectory = _dir.Path, EnableDiscovery = false });
        var connection = new AdminConnectionService(options, new AgentStateStore(_dir.Path),
            AgentIdentity.LoadOrCreate(_dir.Path, NullLogger.Instance), new SystemStatusCollector(),
            NullLogger<AdminConnectionService>.Instance);
        using var server = new LocalStatusServer(connection, NullLogger<LocalStatusServer>.Instance, pipeName);
        await server.StartAsync(ct);

        // Mehrere Abfragen hintereinander: der Server muss für jede eine neue Instanz bereitstellen.
        for (var i = 0; i < 3; i++)
        {
            var status = await LocalStatusPipe.QueryAsync(TimeSpan.FromSeconds(5), ct, pipeName);
            Assert.NotNull(status);
            Assert.Equal(AgentLinkState.Searching, status.State);
            Assert.Null(status.PairingCode);
            Assert.Equal(Environment.MachineName, status.MachineName);
        }

        await server.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Query_ReturnsNull_WhenAgentIsNotRunning()
    {
        var status = await LocalStatusPipe.QueryAsync(TimeSpan.FromMilliseconds(300),
            TestContext.Current.CancellationToken, "MorniLAN.Test.Gibtsnicht." + Guid.NewGuid().ToString("N"));
        Assert.Null(status);
    }
}
