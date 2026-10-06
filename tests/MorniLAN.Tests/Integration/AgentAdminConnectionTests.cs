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

    /// <summary>
    /// M4 komplett über TLS: sperren, während der PC online ist; ändern, während er offline ist (holt er beim
    /// Verbinden ab); eigener Eintrag kommt mit Icon in der Programmliste zurück; Entkoppeln hebt alles auf.
    /// </summary>
    [Fact]
    public async Task Policy_ReachesAgent_Online_Offline_CustomApps_AndUnpair()
    {
        await using var admin = await StartAdminAsync(_adminDir.Path);
        var options = new AgentConnectionOptions
        {
            DataDirectory = _agentDir.Path,
            AdminHost = "127.0.0.1",
            AdminPort = admin.Port,
            EnableDiscovery = false,
            HeartbeatInterval = TimeSpan.FromMilliseconds(300),
        };
        MorniLAN.Agent.Policy.AgentPolicyStore NewPolicyStore() => new(_agentDir.Path);
        InventoryService NewInventory(MorniLAN.Agent.Policy.AgentPolicyStore policy) =>
            new(NullLogger<InventoryService>.Instance, () => new InventoryCollector.Result(
            [
                new InventoryItem(new AppEntry("exe:discord", "Discord", AppSource.InstalledProgram,
                    ExecutablePath: @"C:\D\Update.exe"), new ImageSources()),
                new InventoryItem(new AppEntry("steam:427520", "Factorio", AppSource.Steam, SteamAppId: 427520), new ImageSources()),
            ], []), policy: policy);

        var policy = NewPolicyStore();
        var agent = await StartAgentAsync(options, inventory: NewInventory(policy), policy: policy);
        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
        await admin.Pairing.SubmitCodeAsync(admin.Pairing.Snapshot()[0].RequestId, agent.PairingCode!);
        var deviceId = new AgentStateStore(_agentDir.Path).Current.DeviceId;
        await WaitUntil(() => agent.State == AgentLinkState.Online, "online");

        // 1. Online sperren: kommt sofort an und wird bestätigt
        Assert.True(await admin.UpdatePolicyAsync(deviceId, (p, now) => p.WithAllowed(["exe:discord"], false, now)));
        await WaitUntil(() => !policy.Current.IsAllowed("exe:discord") && admin.Policies.IsApplied(deviceId), "Sperre auf dem PC");

        // 2. Eigener Eintrag: kommt in der Programmliste des PCs zurück, mit Icon aus der EXE
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"); // hat ein Icon
        Assert.True(await admin.UpdatePolicyAsync(deviceId,
            (p, now) => p.WithCustomApp(new CustomApp("custom:test", "Mein Tool", exe), now)));
        await WaitUntil(() => admin.Inventory.Get(deviceId)?.Apps.Any(a => a.Id == "custom:test") == true,
            "eigener Eintrag in der Liste");
        var custom = admin.Inventory.Get(deviceId)!.Apps.Single(a => a.Id == "custom:test");
        Assert.Equal(AppSource.Custom, custom.Source);
        Assert.NotNull(custom.IconHash);

        // 3. PC aus, Admin gibt Discord wieder frei: gespeichert, aber noch nicht angekommen
        await StopAsync(agent);
        await WaitUntil(() => admin.Registry.ConnectionIdOf(deviceId) is null, "getrennt");
        Assert.False(await admin.UpdatePolicyAsync(deviceId, (p, now) => p.WithAllowed(["exe:discord"], true, now)));
        Assert.False(admin.Policies.IsApplied(deviceId));

        // 4. PC wieder an (neuer Prozess): holt den Stand beim Verbinden ab
        policy = NewPolicyStore();
        Assert.False(policy.Current.IsAllowed("exe:discord")); // bis dahin gilt der gespeicherte Stand
        agent = await StartAgentAsync(options, inventory: NewInventory(policy), policy: policy);
        await WaitUntil(() => policy.Current.IsAllowed("exe:discord") && admin.Policies.IsApplied(deviceId), "Stand nachgeholt");
        Assert.Single(policy.Current.CustomApps);

        // 5. Entkoppeln: ohne Admin gelten keine Freigaben mehr
        await admin.UnpairAsync(deviceId);
        await WaitUntil(() => policy.Current.Revision == 0 && policy.Current.CustomApps.Length == 0, "Freigaben aufgehoben");
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

    /// <summary>
    /// M5 über TLS: Profil im Launcher angelegt (Briefkasten) kommt im Panel an; Panel legt eins an und löscht es;
    /// „Hilfe anfordern“ erscheint im Panel, auch wenn das Panel beim Drücken noch aus war.
    /// </summary>
    [Fact]
    public async Task Profiles_And_HelpRequests_ReachTheAdmin()
    {
        var user = Path.Combine(_agentDir.Path, "Users", "Freund");
        var inboxFolder = LauncherInbox.FolderFor(user);
        var policy = new MorniLAN.Agent.Policy.AgentPolicyStore(_agentDir.Path);
        var profiles = new MorniLAN.Agent.Policy.AgentProfileStore(_agentDir.Path);
        var inbox = new MorniLAN.Agent.Policy.LauncherInboxService(profiles, policy,
            NullLogger<MorniLAN.Agent.Policy.LauncherInboxService>.Instance, () => [user]);

        // Hilfe gedrückt, bevor es überhaupt ein Panel gibt: wartet
        LauncherInbox.WriteHelpRequest(inboxFolder, new LauncherInbox.HelpMessage("Lena"));
        inbox.ProcessOnce();
        Assert.NotNull(inbox.PendingHelp);

        await using var admin = await StartAdminAsync(_adminDir.Path);
        var options = new AgentConnectionOptions
        {
            DataDirectory = _agentDir.Path,
            AdminHost = "127.0.0.1",
            AdminPort = admin.Port,
            EnableDiscovery = false,
            HeartbeatInterval = TimeSpan.FromMilliseconds(300),
        };
        var agent = await StartAgentAsync(options, policy: policy, profiles: profiles, inbox: inbox);
        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
        await admin.Pairing.SubmitCodeAsync(admin.Pairing.Snapshot()[0].RequestId, agent.PairingCode!);
        var deviceId = new AgentStateStore(_agentDir.Path).Current.DeviceId;

        // Wartende Hilfe kommt nach dem Verbinden an
        await WaitUntil(() => admin.Help.Open.Count == 1 && inbox.Help?.Delivered == true, "Hilfe nachgereicht");
        Assert.Equal("Lena", admin.Help.Open[0].Request.ProfileName);

        // Profil im Launcher angelegt
        LauncherInbox.WriteProfileRequest(inboxFolder, new LauncherInbox.ProfileRequest("Lena", "#3DDC84"));
        inbox.ProcessOnce();
        await WaitUntil(() => admin.Profiles.Get(deviceId).Any(p => p.Name == "Lena"), "Profil im Panel");

        // Panel legt eins an und löscht es wieder (samt eigener Freigaben)
        Assert.True(await admin.CreateProfileAsync(deviceId, "Max", "#4C8DFF"));
        await WaitUntil(() => profiles.Current.Any(p => p.Name == "Max") && admin.Profiles.Get(deviceId).Length == 2, "Profil vom Panel");
        var max = profiles.Current.Single(p => p.Name == "Max");
        await admin.UpdatePolicyAsync(deviceId, (p, now) => p.WithAllowedForProfile(max.Id, ["steam:1"], false, now));
        await WaitUntil(() => !policy.Current.IsAllowed("steam:1", max.Id), "Regel für Max");
        Assert.True(await admin.DeleteProfileAsync(deviceId, max.Id));
        await WaitUntil(() => profiles.Current.All(p => p.Id != max.Id) && policy.Current.ProfileRules is not { Length: > 0 },
            "Profil und Regeln gelöscht");

        await StopAsync(agent);
    }

    /// <summary>M6 über TLS: Konten werden gemeldet; der Admin schränkt eins ein und sperrt einen Bereich; „Sperren jetzt“ kommt an.</summary>
    [Fact]
    public async Task Restrictions_ReportAccounts_SetFromPanel_AndApplyNow()
    {
        await using var admin = await StartAdminAsync(_adminDir.Path);
        var policy = new MorniLAN.Agent.Policy.AgentPolicyStore(_agentDir.Path);
        var accounts = new[]
        {
            new LocalAccount("S-1-5-21-900", "Freund", false),
            new LocalAccount("S-1-5-21-901", "Chef", true),
        };
        var applied = 0;
        var restrictions = new MorniLAN.Agent.Restrictions.RestrictionService(policy,
            NullLogger<MorniLAN.Agent.Restrictions.RestrictionService>.Instance,
            writePolicies: (_, _) => Interlocked.Increment(ref applied), clearPolicies: _ => { },
            terminate: _ => { }, enumerate: () => [], scanAccounts: () => accounts);

        var options = new AgentConnectionOptions
        {
            DataDirectory = _agentDir.Path, AdminHost = "127.0.0.1", AdminPort = admin.Port,
            EnableDiscovery = false, HeartbeatInterval = TimeSpan.FromMilliseconds(300),
        };
        var agent = await StartAgentAsync(options, policy: policy, restrictions: restrictions);
        using var cts = new CancellationTokenSource();
        await restrictions.StartAsync(cts.Token);
        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
        await admin.Pairing.SubmitCodeAsync(admin.Pairing.Snapshot()[0].RequestId, agent.PairingCode!);
        var deviceId = new AgentStateStore(_agentDir.Path).Current.DeviceId;

        // Konten kommen im Panel an (inkl. Admin-Kennzeichen)
        await WaitUntil(() => admin.Accounts.Get(deviceId).Length == 2, "Konten im Panel");
        Assert.True(admin.Accounts.Get(deviceId).Single(a => a.Name == "Chef").IsAdministrator);

        // Admin schränkt „Freund“ ein und sperrt die Konsole → der Agent setzt Richtlinien
        var before = applied;
        await admin.UpdatePolicyAsync(deviceId, (p, now) =>
            p.WithRestrictedAccount("S-1-5-21-900", true, now).WithBlockedArea(WindowsAreas.Console, true, now));
        await WaitUntil(() => applied > before, "Richtlinien gesetzt");
        await WaitUntil(() => admin.Accounts.State(deviceId).RestrictedAccountCount == 1, "Stand gemeldet");

        // „Sperren jetzt aktivieren“
        var before2 = applied;
        Assert.True(await admin.ApplyRestrictionsAsync(deviceId));
        await WaitUntil(() => applied > before2, "erneut angewandt");

        await cts.CancelAsync();
        await restrictions.StopAsync(CancellationToken.None);
        await StopAsync(agent);
    }

    /// <summary>M7 über TLS: Panel startet Fernzugriff (ohne Rückfrage), Agent wird aktiv und meldet den Stand; Eingabesperre; Trennen.</summary>
    [Fact]
    public async Task Remote_StartWithoutConsent_InputLock_Stop()
    {
        await using var admin = await StartAdminAsync(_adminDir.Path);
        var remote = new MorniLAN.Agent.Remote.RemoteAccessService(
            NullLogger<MorniLAN.Agent.Remote.RemoteAccessService>.Instance,
            ensureSunshine: () => true, sunshineInstalled: () => true, hostAddress: () => "10.0.0.5:47989");
        var options = new AgentConnectionOptions
        {
            DataDirectory = _agentDir.Path, AdminHost = "127.0.0.1", AdminPort = admin.Port,
            EnableDiscovery = false, HeartbeatInterval = TimeSpan.FromMilliseconds(300),
        };
        var agent = await StartAgentAsync(options, remote: remote);
        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
        await admin.Pairing.SubmitCodeAsync(admin.Pairing.Snapshot()[0].RequestId, agent.PairingCode!);
        var deviceId = new AgentStateStore(_agentDir.Path).Current.DeviceId;
        await WaitUntil(() => agent.State == AgentLinkState.Online, "online");

        admin.Remote.SetAllowWithoutConsent(deviceId, true);
        Assert.True(await admin.StartRemoteAsync(deviceId));
        await WaitUntil(() => admin.Remote.State(deviceId).Phase == RemoteSessionPhase.Active, "Fernzugriff aktiv im Panel");
        Assert.Equal("10.0.0.5:47989", admin.Remote.State(deviceId).Host);

        Assert.True(await admin.SetInputLockAsync(deviceId, true));
        await WaitUntil(() => admin.Remote.State(deviceId).InputLocked, "Eingabe gesperrt");

        Assert.True(await admin.StopRemoteAsync(deviceId));
        await WaitUntil(() => admin.Remote.State(deviceId).Phase == RemoteSessionPhase.Idle, "beendet");

        await StopAsync(agent);
    }

    /// <summary>M8 über TLS: Panel schickt einen Befehl (Nachricht), Agent führt ihn aus und meldet das Ergebnis.</summary>
    [Fact]
    public async Task Command_RunsOnAgent_AndResultReachesPanel()
    {
        await using var admin = await StartAdminAsync(_adminDir.Path);
        var messages = new List<MorniLAN.Shared.Models.AdminMessage>();
        var actions = new MorniLAN.Agent.Actions.ActionService(NullLogger<MorniLAN.Agent.Actions.ActionService>.Instance,
            runProcess: (_, _, _) => (0, ""), showMessage: messages.Add);
        var options = new AgentConnectionOptions
        {
            DataDirectory = _agentDir.Path, AdminHost = "127.0.0.1", AdminPort = admin.Port,
            EnableDiscovery = false, HeartbeatInterval = TimeSpan.FromMilliseconds(300),
        };
        var agent = await StartAgentAsync(options, actions: actions);
        await WaitUntil(() => admin.Pairing.Snapshot().Count == 1 && agent.PairingCode is not null, "Pairing-Anfrage");
        await admin.Pairing.SubmitCodeAsync(admin.Pairing.Snapshot()[0].RequestId, agent.PairingCode!);
        var deviceId = new AgentStateStore(_agentDir.Path).Current.DeviceId;
        await WaitUntil(() => agent.State == AgentLinkState.Online, "online");

        var command = new MorniLAN.Shared.Models.ShowMessageCommand("Essen", "Gleich Abendessen");
        Assert.True(await admin.RunCommandAsync(deviceId, command));
        await WaitUntil(() => admin.Actions.For(deviceId).Any(e => e.CommandId == command.CommandId && e.Success == true),
            "Ergebnis im Panel");
        Assert.Equal("Gleich Abendessen", Assert.Single(messages).Text);

        await StopAsync(agent);
    }

    private static async Task<AdminConnectionService> StartAgentAsync(AgentConnectionOptions options,
        DiscoveryListener? discovery = null, InventoryService? inventory = null,
        MorniLAN.Agent.Policy.AgentPolicyStore? policy = null, MorniLAN.Agent.Policy.AgentProfileStore? profiles = null,
        MorniLAN.Agent.Policy.LauncherInboxService? inbox = null, MorniLAN.Agent.Restrictions.RestrictionService? restrictions = null,
        MorniLAN.Agent.Remote.RemoteAccessService? remote = null, MorniLAN.Agent.Actions.ActionService? actions = null)
    {
        var identity = AgentIdentity.LoadOrCreate(options.DataDirectory, NullLogger.Instance);
        var service = new AdminConnectionService(Options.Create(options), new AgentStateStore(options.DataDirectory),
            identity, new SystemStatusCollector(), NullLogger<AdminConnectionService>.Instance, discovery, inventory,
            policy: policy, profiles: profiles, inbox: inbox, restrictions: restrictions, remote: remote, actions: actions);
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
