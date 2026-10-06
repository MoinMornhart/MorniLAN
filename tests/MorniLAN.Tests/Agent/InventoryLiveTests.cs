using MorniLAN.Agent.Inventory;

namespace MorniLAN.Tests.Agent;

/// <summary>
/// Läuft gegen den echten PC. Nur auf Wunsch: MorniLAN.Tests.exe -explicit only -method "*Live*".
/// Zeigt, was der Agent auf diesem Rechner finden würde.
/// </summary>
public class InventoryLiveTests
{
    [Fact(Explicit = true)]
    public void Live_CollectOnThisPc()
    {
        var result = InventoryCollector.Collect();
        var output = TestContext.Current.TestOutputHelper!;
        foreach (var problem in result.Problems)
            output.WriteLine("PROBLEM " + problem);
        foreach (var group in result.Items.GroupBy(i => (i.App.Source, i.App.IsSystemComponent)).OrderBy(g => g.Key.Source))
            output.WriteLine($"{group.Key.Source} {(group.Key.IsSystemComponent ? "(System)" : "")}: {group.Count()}");
        foreach (var item in result.Items.Where(i => !i.App.IsSystemComponent))
            output.WriteLine($"  {item.App.Source,-16} {item.App.Name} | {item.App.LaunchTarget} | Bild: {item.Images.CoverFile ?? item.Images.IconFile ?? item.Images.IconFromExecutable ?? "-"}");
        output.WriteLine("--- als System ausgeblendet:");
        foreach (var item in result.Items.Where(i => i.App.IsSystemComponent))
            output.WriteLine($"  {item.App.Name}");
        Assert.NotEmpty(result.Items);
    }

    /// <summary>
    /// Stellt die beiden Launcher-Pipes unter eigenen Namen bereit, mit der echten Liste dieses PCs, ohne den Dienst
    /// anzufassen. Launcher dazu mit MORNILAN_PIPE=MorniLAN.Preview und MORNILAN_APPS_PIPE=MorniLAN.Preview.Apps
    /// starten. Läuft MORNILAN_PREVIEW_SECONDS lang (Standard 90). MORNILAN_PREVIEW_BLOCK: Namen, die gesperrt sind.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Live_LauncherPipesForPreview()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = Directory.CreateTempSubdirectory("mornilan-launcher-preview");
        var policy = new MorniLAN.Agent.Policy.AgentPolicyStore(dir.FullName);
        var inventory = new InventoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryService>.Instance,
            InventoryCollector.Collect, new StoreCoverService(Path.Combine(dir.FullName, "covers")), policy);
        var report = await inventory.RefreshAsync(ct);
        var blockNames = (Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_BLOCK") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var blocked = report.Apps.Where(a => blockNames.Contains(a.Name, StringComparer.OrdinalIgnoreCase)).Select(a => a.Id).ToList();
        policy.Apply(MorniLAN.Shared.Models.AppPolicy.Default.WithAllowed(blocked, false, DateTimeOffset.UtcNow));
        // Profile und Briefkasten (M5): Launcher dazu mit MORNILAN_INBOX=<MORNILAN_PREVIEW_USER>\AppData\Local\MorniLAN\launcher\inbox
        var profiles = new MorniLAN.Agent.Policy.AgentProfileStore(dir.FullName);
        var previewUser = Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_USER")
                          ?? Path.Combine(Path.GetTempPath(), "mornilan-preview-user");
        // Fernzugriff (M7): MORNILAN_PREVIEW_REMOTE=ask zeigt die Erlauben-Abfrage, =active den aktiven Balken
        var remote = new MorniLAN.Agent.Remote.RemoteAccessService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MorniLAN.Agent.Remote.RemoteAccessService>.Instance,
            ensureSunshine: () => true, sunshineInstalled: () => true, hostAddress: () => "192.168.178.109:47989");
        switch (Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_REMOTE"))
        {
            case "ask": remote.Start(allowWithoutConsent: false); break;
            case "active": remote.Start(allowWithoutConsent: true); break;
        }
        var inbox = new MorniLAN.Agent.Policy.LauncherInboxService(profiles, policy,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MorniLAN.Agent.Policy.LauncherInboxService>.Instance,
            () => [previewUser], remote: remote);
        await inbox.StartAsync(ct);
        var catalog = new MorniLAN.Agent.Policy.LauncherCatalog(inventory, policy, profiles);

        // Nachricht (M8): MORNILAN_PREVIEW_MESSAGE=<Text> zeigt ein Admin-Pop-up
        var actions = new MorniLAN.Agent.Actions.ActionService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MorniLAN.Agent.Actions.ActionService>.Instance,
            runProcess: (_, _, _) => (0, ""));
        if (Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_MESSAGE") is { Length: > 0 } msg)
            await actions.RunAsync(new MorniLAN.Shared.Models.ShowMessageCommand("Nachricht vom Admin", msg), ct);

        var log = Microsoft.Extensions.Logging.Abstractions.NullLogger<MorniLAN.Agent.Connection.LocalStatusServer>.Instance;
        using var status = new MorniLAN.Agent.Connection.LocalStatusServer(() => MorniLAN.Shared.Connection.LocalStatusPipe.Serialize(
            new MorniLAN.Shared.Connection.AgentLocalStatus(MorniLAN.Shared.Connection.AgentLinkState.Online, null, "Vorschau-Panel",
                Environment.MachineName, "Vorschau", DateTimeOffset.UtcNow, catalog.Current().Hash, inbox.Help, remote.State,
                actions.CurrentMessage)), log, "MorniLAN.Preview", grantCurrentUser: true);
        using var apps = new MorniLAN.Agent.Connection.LocalStatusServer(
            () => MorniLAN.Shared.Connection.LauncherAppsPipe.Serialize(catalog.Current()), log, "MorniLAN.Preview.Apps",
            grantCurrentUser: true);
        await status.StartAsync(ct);
        await apps.StartAsync(ct);
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{catalog.Current().Apps.Length} Kacheln, gesperrt: {string.Join(", ", blockNames)}");
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_SECONDS"), out var s) ? s : 90;
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
        await status.StopAsync(CancellationToken.None);
        await apps.StopAsync(CancellationToken.None);
        await inbox.StopAsync(CancellationToken.None);
        try { dir.Delete(recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// Ein Test-Agent (im Testprozess, eigener Datenordner) verbindet sich mit dem Panel auf diesem PC
    /// (127.0.0.1:47950). Der Pairing-Code steht in MORNILAN_LIVE_DIR\code.txt; Hilfe-Anfragen und Profilwünsche
    /// legt man in MORNILAN_LIVE_DIR\user\AppData\Local\MorniLAN\launcher\inbox ab. Läuft MORNILAN_PREVIEW_SECONDS.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Live_AgentForLocalPanel()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Environment.GetEnvironmentVariable("MORNILAN_LIVE_DIR") ?? throw new InvalidOperationException("MORNILAN_LIVE_DIR setzen");
        var data = Path.Combine(root, "agent");
        var user = Path.Combine(root, "user");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(MorniLAN.Shared.Models.LauncherInbox.FolderFor(user));
        var policy = new MorniLAN.Agent.Policy.AgentPolicyStore(data);
        var profiles = new MorniLAN.Agent.Policy.AgentProfileStore(data);
        var inbox = new MorniLAN.Agent.Policy.LauncherInboxService(profiles, policy,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MorniLAN.Agent.Policy.LauncherInboxService>.Instance, () => [user]);
        var inventory = new InventoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryService>.Instance, () => new InventoryCollector.Result(
            [new InventoryItem(new MorniLAN.Shared.Models.AppEntry("steam:427520", "Factorio", MorniLAN.Shared.Models.AppSource.Steam,
                SteamAppId: 427520), new ImageSources())], []), policy: policy);
        // Sperren-Dienst: echte Konten melden, aber NICHTS an der echten Registry ändern (No-Op)
        var restrictions = new MorniLAN.Agent.Restrictions.RestrictionService(policy,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MorniLAN.Agent.Restrictions.RestrictionService>.Instance,
            writePolicies: (_, _) => { }, clearPolicies: _ => { }, terminate: _ => { }, enumerate: () => []);
        var options = new MorniLAN.Agent.Connection.AgentConnectionOptions
        {
            DataDirectory = data, AdminHost = "127.0.0.1", AdminPort = 47950, EnableDiscovery = false,
        };
        // Aktionen (M8): No-Op – echte Programme werden im Vorschau-Test NICHT installiert
        var actions = new MorniLAN.Agent.Actions.ActionService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MorniLAN.Agent.Actions.ActionService>.Instance,
            runProcess: (_, _, _) => (0, "(Vorschau: nichts ausgeführt)"));
        var agent = new MorniLAN.Agent.Connection.AdminConnectionService(Microsoft.Extensions.Options.Options.Create(options),
            new MorniLAN.Agent.Connection.AgentStateStore(data),
            MorniLAN.Agent.Connection.AgentIdentity.LoadOrCreate(data, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance),
            new MorniLAN.Agent.Platform.SystemStatusCollector(), Microsoft.Extensions.Logging.Abstractions.NullLogger<MorniLAN.Agent.Connection.AdminConnectionService>.Instance,
            inventory: inventory, policy: policy, profiles: profiles, inbox: inbox, restrictions: restrictions, actions: actions);
        await inbox.StartAsync(ct);
        using var restrictionsCts = new CancellationTokenSource();
        await restrictions.StartAsync(restrictionsCts.Token);
        await agent.StartAsync(ct);
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_SECONDS"), out var s) ? s : 300;
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "code.txt"), $"{agent.State} {agent.PairingCode}", ct);
            await Task.Delay(1000, ct);
        }
        await agent.StopAsync(CancellationToken.None);
        await restrictionsCts.CancelAsync();
        await restrictions.StopAsync(CancellationToken.None);
        await inbox.StopAsync(CancellationToken.None);
    }

    /// <summary>Fragt die Vorschau-Pipes wie der Launcher ab und zählt Aussetzer.</summary>
    [Fact(Explicit = true)]
    public async Task Live_QueryPreviewPipes()
    {
        var ct = TestContext.Current.CancellationToken;
        var output = TestContext.Current.TestOutputHelper!;
        var failures = new List<string>();
        for (var i = 0; i < 60; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var status = await MorniLAN.Shared.Connection.LocalStatusPipe.QueryAsync(TimeSpan.FromSeconds(1.5), ct, "MorniLAN.Preview");
            if (status is null)
                failures.Add($"#{i} nach {watch.ElapsedMilliseconds} ms");
            if (i % 20 == 0)
            {
                watch.Restart();
                var apps = await MorniLAN.Shared.Connection.LauncherAppsPipe.QueryAsync(TimeSpan.FromSeconds(10), ct, "MorniLAN.Preview.Apps");
                output.WriteLine($"Apps: {apps?.Apps.Length.ToString() ?? "FEHLT"} in {watch.ElapsedMilliseconds} ms");
            }
            await Task.Delay(200, ct);
        }
        output.WriteLine($"Status-Aussetzer: {failures.Count} von 60 {string.Join(", ", failures)}");
    }

    /// <summary>Holt echte Cover aus dem Steam-Shop (braucht Internet).</summary>
    [Fact(Explicit = true)]
    public async Task Live_StoreCovers()
    {
        var dir = Directory.CreateTempSubdirectory("mornilan-live-covers");
        try
        {
            var service = new StoreCoverService(dir.FullName);
            InventoryItem Game(string name, string launcher) => new(
                new MorniLAN.Shared.Models.AppEntry("x:" + name, name, MorniLAN.Shared.Models.AppSource.InstalledProgram,
                    Launcher: launcher), new ImageSources());

            var items = await service.FillAsync(
            [
                Game("Assassin’s Creed® Valhalla", MorniLAN.Shared.Models.GameLaunchers.Ubisoft), // Hochformat
                Game("Battlefield™ 6", MorniLAN.Shared.Models.GameLaunchers.Ea), // nur Platzhalter → Querformat
                Game("Fortnite", MorniLAN.Shared.Models.GameLaunchers.Epic), // gibt es auf Steam nicht
            ], TestContext.Current.CancellationToken);

            foreach (var item in items)
                TestContext.Current.TestOutputHelper!.WriteLine(
                    $"{item.App.Name}: {(item.Images.CoverFile is { } f ? $"{new FileInfo(f).Length:N0} Bytes" : "kein Cover")}");
            Assert.True(new FileInfo(items[0].Images.CoverFile!).Length > 20_000);
            Assert.True(new FileInfo(items[1].Images.CoverFile!).Length > 20_000);
            Assert.Null(items[2].Images.CoverFile);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Füllt ein Admin-Datenverzeichnis mit der echten Liste dieses PCs (als Gerät „Vorschau“), um die Seite
    /// „Freigaben“ ohne zweiten PC anzusehen. Ziel: Umgebungsvariable MORNILAN_PREVIEW_DIR.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Live_FillAdminDataForPreview()
    {
        var target = Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_DIR")
                     ?? throw new InvalidOperationException("MORNILAN_PREVIEW_DIR setzen");
        // MORNILAN_PREVIEW_SAMPLES=1: zusätzlich Beispiel-Spiele aus den anderen Launchern (gibt es auf diesem PC nicht)
        var samples = Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_SAMPLES") == "1";
        var service = samples
            ? new InventoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryService>.Instance,
                () =>
                {
                    var real = InventoryCollector.Collect();
                    return real with { Items = [.. real.Items, .. SampleLauncherGames()] };
                },
                new StoreCoverService(Path.Combine(target, "preview-covers")))
            : new InventoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryService>.Instance);
        var report = await service.RefreshAsync(TestContext.Current.CancellationToken);

        var store = new MorniLAN.Admin.Server.InventoryStore(target);
        var deviceId = Guid.Parse("00000000-0000-0000-0000-00000000beef");
        new MorniLAN.Admin.Server.DeviceRegistry(target).Add(
            new MorniLAN.Admin.Server.PairedDevice(deviceId, Environment.MachineName + " (Vorschau)", new string('0', 64), DateTimeOffset.UtcNow));
        store.Save(deviceId, report);
        // Beispiel-Konten, damit die Konten-Karten (mit Einschränken- und Kiosk-Schalter) in der Vorschau erscheinen
        new MorniLAN.Admin.Server.DeviceAccountStore(target).Save(deviceId,
        [
            new MorniLAN.Shared.Models.LocalAccount("S-1-5-21-vorschau-1001", "Freund", false),
            new MorniLAN.Shared.Models.LocalAccount("S-1-5-21-vorschau-500", "Chef (du)", true),
        ]);
        var saved = 0;
        foreach (var hash in store.MissingImages(report))
            if (service.GetImage(hash) is { } image && store.TrySaveImage(image))
                saved++;
        TestContext.Current.TestOutputHelper!.WriteLine($"{report.Apps.Length} Einträge, {saved} Bilder nach {target}");
    }

    private static IEnumerable<InventoryItem> SampleLauncherGames()
    {
        (string Name, string Launcher, string Prefix)[] games =
        [
            ("Fortnite", MorniLAN.Shared.Models.GameLaunchers.Epic, "epic"),
            ("Hogwarts Legacy", MorniLAN.Shared.Models.GameLaunchers.Epic, "epic"),
            ("The Witcher 3: Wild Hunt", MorniLAN.Shared.Models.GameLaunchers.Gog, "gog"),
            ("Assassin’s Creed® Valhalla", MorniLAN.Shared.Models.GameLaunchers.Ubisoft, "ubisoft"),
            ("Battlefield™ 6", MorniLAN.Shared.Models.GameLaunchers.Ea, "ea"),
            ("Overwatch® 2", MorniLAN.Shared.Models.GameLaunchers.BattleNet, "bnet"),
        ];
        return games.Select(g => new InventoryItem(
            new MorniLAN.Shared.Models.AppEntry(MorniLAN.Shared.Models.AppId.ForLauncher(g.Prefix, g.Name), g.Name,
                MorniLAN.Shared.Models.AppSource.InstalledProgram, Publisher: "Beispiel für die Vorschau", Launcher: g.Launcher),
            new ImageSources()));
    }
}
