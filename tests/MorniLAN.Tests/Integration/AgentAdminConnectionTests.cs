using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MorniLAN.Admin.Server;
using MorniLAN.Agent.Connection;
using MorniLAN.Agent.Inventory;
using MorniLAN.Agent.Platform;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Security;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Integration;

/// <summary>
/// Echte Verbindung über TLS (Loopback): Admin-Server und Agent-Dienst im selben Prozess.
/// LAN-Beacon ist aus, der Agent bekommt die Adresse fest eingestellt (wie bei Tailscale).
/// </summary>
public sealed class AgentAdminConnectionTests : IDisposable
{
    private readonly TempDirectory _adminDir = new();
    private readonly TempDirectory _agentDir = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _adminDir.Dispose();
        _agentDir.Dispose();
    }

    [Fact]
    public async Task Pairing_Heartbeat_Reconnect_And_Unpair()
    {
        await using var admin = await StartAdminAsync(_adminDir.Path);

        // 1. Unbekannter Agent meldet sich und wartet auf den Code.
        var agent = await StartAgentAsync(admin.Port);
        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
        // Das Panel sieht die Anfrage einen Augenblick, bevor der Agent seinen Zustand umstellt.
        await WaitUntil(() => agent.State == AgentLinkState.WaitingForPairing, "Agent wartet auf Pairing");
        var request = admin.Pairing.Snapshot()[0];
        Assert.Equal(Environment.MachineName, request.Device.MachineName);

        // 2. Admin tippt den Code ein → gekoppelt, online, Heartbeats kommen an.
        Assert.Equal(PairingSubmitResult.Approved,
            await admin.Pairing.SubmitCodeAsync(request.RequestId, PairingCode.Format(agent.PairingCode!)));
        await WaitUntil(() => agent.State == AgentLinkState.Online, "Agent online");
        await WaitUntil(() => admin.Registry.Snapshot() is [{ Status: not null } d]
                              && d.PresenceAt(DateTimeOffset.UtcNow) == DevicePresence.Online, "Heartbeat im Panel");
        var device = Assert.Single(admin.Registry.Snapshot());
        Assert.Equal(request.AgentFingerprint, device.Device.Fingerprint);
        Assert.True(device.Status!.RamTotalBytes > 0);
        Assert.Null(agent.PairingCode);

        // 3. Agent beendet → Panel sieht ihn offline.
        await StopAsync(agent);
        await WaitUntil(() => admin.Registry.Snapshot()[0].Connected == false, "Agent getrennt");

        // 4. Neustart des Agents → ohne neuen Code wieder online (Pin + gespeichertes Zertifikat).
        agent = await StartAgentAsync(admin.Port);
        await WaitUntil(() => agent.State == AgentLinkState.Online, "Agent wieder online");
        Assert.Empty(admin.Pairing.Snapshot());

        // 5. Admin entfernt den PC → Agent vergisst den Pin und bittet erneut um Pairing.
        await admin.UnpairAsync(device.Device.DeviceId);
        Assert.Empty(admin.Registry.Snapshot());
        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "neue Pairing-Anfrage");
        // Das Panel sieht die Anfrage einen Augenblick, bevor der Agent seinen Zustand umstellt.
        await WaitUntil(() => agent.State == AgentLinkState.WaitingForPairing, "Agent wartet auf Pairing");

        await StopAsync(agent);
    }

    [Fact]
    public async Task TooManyWrongCodes_RejectsAndAgentShowsNewCode()
    {
        await using var admin = await StartAdminAsync(_adminDir.Path);
        var agent = await StartAgentAsync(admin.Port);
        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
        var firstCode = agent.PairingCode;
        var requestId = admin.Pairing.Snapshot()[0].RequestId;

        for (var i = 1; i < ConnectionDefaults.MaxPairingAttempts; i++)
            Assert.Equal(PairingSubmitResult.WrongCode, await admin.Pairing.SubmitCodeAsync(requestId, "AAAA-AAAA"));
        Assert.Equal(PairingSubmitResult.TooManyAttempts, await admin.Pairing.SubmitCodeAsync(requestId, "AAAA-AAAA"));

        await WaitUntil(() => agent.PairingCode is { } code && code != firstCode, "neuer Code");
        await WaitUntil(() => admin.Pairing.Snapshot() is [{ } p] && p.RequestId != requestId, "neue Anfrage");
        Assert.Empty(admin.Registry.Snapshot());

        await StopAsync(agent);
    }

    [Fact]
    public async Task PairedAgent_RefusesAdminPanelWithOtherCertificate()
    {
        int port;
        await using (var admin = await StartAdminAsync(_adminDir.Path))
        {
            port = admin.Port;
            var agent = await StartAgentAsync(port);
            await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
            await admin.Pairing.SubmitCodeAsync(admin.Pairing.Snapshot()[0].RequestId, agent.PairingCode!);
            await WaitUntil(() => agent.State == AgentLinkState.Online, "Agent online");
            await StopAsync(agent);
        }

        // Fremdes Panel (anderes Zertifikat) auf derselben Adresse.
        using var otherDir = new TempDirectory();
        await using var impostor = await StartAdminAsync(otherDir.Path, port);
        Assert.NotEqual(CertificateFingerprint.Of(impostor.Identity.Certificate), PinnedFingerprint());

        var reconnecting = await StartAgentAsync(port);
        await Task.Delay(TimeSpan.FromSeconds(3), Ct);
        Assert.NotEqual(AgentLinkState.Online, reconnecting.State);
        Assert.Empty(impostor.Pairing.Snapshot());
        Assert.Empty(impostor.Registry.Snapshot());
        Assert.NotNull(PinnedFingerprint()); // Pin bleibt erhalten
        await StopAsync(reconnecting);
    }

    [Fact]
    public async Task Agent_FindsAdminViaDiscoveryQuery_WithoutConfiguredHost()
    {
        // Panel und Agent auf getrennten UDP-Ports: Die periodischen Beacons des Panels erreichen den
        // Agent also nicht, er findet es nur über seine eigene Suchanfrage und die direkte Antwort.
        var adminUdp = FreeUdpPort();
        var agentUdp = FreeUdpPort();
        await using var admin = await StartAdminAsync(_adminDir.Path, discoveryPort: adminUdp);

        var options = new AgentConnectionOptions
        {
            DataDirectory = _agentDir.Path,
            AdminHost = null,
            EnableDiscovery = true,
            DiscoveryPort = agentUdp,
            DiscoveryQueryInterval = TimeSpan.FromMilliseconds(200),
            DiscoveryQueryTargets = [new IPEndPoint(IPAddress.Loopback, adminUdp)],
            HeartbeatInterval = TimeSpan.FromMilliseconds(300),
        };
        using var discovery = new DiscoveryListener(Options.Create(options), NullLogger<DiscoveryListener>.Instance);
        await discovery.StartAsync(Ct);
        var agent = await StartAgentAsync(options, discovery);

        await WaitUntil(() => discovery.Recent().Count > 0, "Antwort auf Suchanfrage");
        var seen = discovery.Recent()[0];
        Assert.Equal(admin.Port, seen.Beacon.Port);
        Assert.Equal(admin.Identity.Fingerprint, seen.Beacon.Fingerprint);

        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
        await admin.Pairing.SubmitCodeAsync(admin.Pairing.Snapshot()[0].RequestId, agent.PairingCode!);
        await WaitUntil(() => agent.State == AgentLinkState.Online, "Agent online über LAN-Suche");

        await StopAsync(agent);
        await discovery.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Inventory_ReachesAdmin_WithImages_AndRefreshOnRequest()
    {
        await using var admin = await StartAdminAsync(_adminDir.Path);

        // Testbilder: ein 32×32-PNG als Icon, ein 600×900-JPEG als Cover
        var iconFile = Path.Combine(_agentDir.Path, "icon.png");
        var coverFile = Path.Combine(_agentDir.Path, "cover.jpg");
        using (var bmp = new System.Drawing.Bitmap(32, 32))
            bmp.Save(iconFile, System.Drawing.Imaging.ImageFormat.Png);
        using (var bmp = new System.Drawing.Bitmap(600, 900))
            bmp.Save(coverFile, System.Drawing.Imaging.ImageFormat.Jpeg);

        var collects = 0;
        var inventory = new InventoryService(NullLogger<InventoryService>.Instance, () =>
        {
            Interlocked.Increment(ref collects);
            return new InventoryCollector.Result(
            [
                new InventoryItem(new AppEntry("steam:427520", "Factorio", AppSource.Steam, SteamAppId: 427520),
                    new ImageSources(IconFile: iconFile, CoverFile: coverFile)),
                new InventoryItem(new AppEntry("exe:1", "Ohne Bild", AppSource.InstalledProgram), new ImageSources()),
            ], []);
        });

        var agent = await StartAgentAsync(new AgentConnectionOptions
        {
            DataDirectory = _agentDir.Path,
            AdminHost = "127.0.0.1",
            AdminPort = admin.Port,
            EnableDiscovery = false,
            HeartbeatInterval = TimeSpan.FromMilliseconds(300),
        }, inventory: inventory);
        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
        await admin.Pairing.SubmitCodeAsync(admin.Pairing.Snapshot()[0].RequestId, agent.PairingCode!);
        var deviceId = new AgentStateStore(_agentDir.Path).Current.DeviceId;

        await WaitUntil(() => admin.Inventory.Get(deviceId) is { } r
                              && admin.Inventory.ImagePath(r.Apps[0].IconHash) is not null
                              && admin.Inventory.ImagePath(r.Apps[0].CoverHash) is not null, "Liste und Bilder im Panel");
        var report = admin.Inventory.Get(deviceId)!;
        Assert.Equal(["Factorio", "Ohne Bild"], report.Apps.Select(a => a.Name));
        var factorio = report.Apps[0];
        Assert.EndsWith(".png", admin.Inventory.ImagePath(factorio.IconHash));
        Assert.EndsWith(".jpg", admin.Inventory.ImagePath(factorio.CoverHash));
        using (var cover = System.Drawing.Image.FromFile(admin.Inventory.ImagePath(factorio.CoverHash)!))
            Assert.Equal((300, 450), (cover.Width, cover.Height)); // auf Cover-Größe verkleinert

        // "Aktualisieren" im Panel liest auf dem PC neu ein
        var before = collects;
        Assert.True(await admin.RequestInventoryRefreshAsync(deviceId));
        await WaitUntil(() => collects > before, "erneutes Einlesen");

        await StopAsync(agent);
    }

    [Fact]
    public void InventoryStore_RejectsImages_ThatDoNotMatchTheirHash()
    {
        var store = new InventoryStore(_adminDir.Path);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png));

        Assert.False(store.TrySaveImage(new AppImage(new string('a', 64), "image/png", png)));
        Assert.False(store.TrySaveImage(new AppImage(hash, "image/svg+xml", png)));
        Assert.False(store.TrySaveImage(new AppImage("../../etc", "image/png", png)));
        Assert.True(store.TrySaveImage(new AppImage(hash, "image/png", png)));
        Assert.NotNull(store.ImagePath(hash));
    }

    private string? PinnedFingerprint() => new AgentStateStore(_agentDir.Path).Current.Admin?.Fingerprint;

    private static async Task<AdminServer> StartAdminAsync(string dataDirectory, int port = 0, int? discoveryPort = null)
    {
        var server = new AdminServer(new AdminServerOptions
        {
            DataDirectory = dataDirectory,
            BindAddress = IPAddress.Loopback,
            Port = port,
            EnableDiscoveryBeacon = discoveryPort is not null,
            DiscoveryPort = discoveryPort ?? 0,
            Name = "TEST-ADMIN",
        });
        await server.StartAsync(Ct);
        return server;
    }

    private Task<AdminConnectionService> StartAgentAsync(int adminPort) =>
        StartAgentAsync(new AgentConnectionOptions
        {
            DataDirectory = _agentDir.Path,
            AdminHost = "127.0.0.1",
            AdminPort = adminPort,
            EnableDiscovery = false,
            HeartbeatInterval = TimeSpan.FromMilliseconds(300),
        });

    private static async Task<AdminConnectionService> StartAgentAsync(AgentConnectionOptions options,
        DiscoveryListener? discovery = null, InventoryService? inventory = null)
    {
        var identity = AgentIdentity.LoadOrCreate(options.DataDirectory, NullLogger.Instance);
        var service = new AdminConnectionService(Options.Create(options), new AgentStateStore(options.DataDirectory),
            identity, new SystemStatusCollector(), NullLogger<AdminConnectionService>.Instance, discovery, inventory);
        await service.StartAsync(Ct);
        return service;
    }

    private static int FreeUdpPort()
    {
        using var udp = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    private static async Task StopAsync(AdminConnectionService agent)
    {
        await agent.StopAsync(CancellationToken.None);
        agent.Dispose();
    }

    private static async Task WaitUntil(Func<bool> condition, string what, int timeoutSeconds = 20)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(timeoutSeconds))
                Assert.Fail($"Zeitüberschreitung beim Warten auf: {what}");
            await Task.Delay(50, Ct);
        }
    }
}
