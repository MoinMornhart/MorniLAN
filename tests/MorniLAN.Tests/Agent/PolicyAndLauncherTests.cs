using Microsoft.Extensions.Logging.Abstractions;
using MorniLAN.Admin.Server;
using MorniLAN.Agent.Connection;
using MorniLAN.Agent.Policy;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Agent;

public sealed class PolicyAndLauncherTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static AppPolicy Blocking(params string[] ids) => AppPolicy.Default.WithAllowed(ids, false, T0);

    [Fact]
    public void AgentStore_KeepsPolicyAcrossRestarts()
    {
        Assert.True(new AgentPolicyStore(_dir.Path).Apply(Blocking("exe:1")));

        var reopened = new AgentPolicyStore(_dir.Path);
        Assert.False(reopened.Current.IsAllowed("exe:1"));
        Assert.Equal(1, reopened.Current.Revision);
    }

    [Fact]
    public void AgentStore_IgnoresOlderPushes_ButAcceptsTheStateFetchedOnConnect()
    {
        var store = new AgentPolicyStore(_dir.Path);
        var newer = Blocking("exe:1").WithAllowed(["exe:2"], false, T0); // Stand 2
        Assert.True(store.Apply(newer));

        // Verspätete Änderung mit Stand 1: ignorieren
        Assert.False(store.Apply(Blocking("exe:3")));
        Assert.False(store.Current.IsAllowed("exe:2"));

        // Panel neu eingerichtet, fängt bei 1 an: Was es beim Verbinden sagt, gilt
        Assert.True(store.Apply(Blocking("exe:9"), authoritative: true));
        Assert.True(store.Current.IsAllowed("exe:2"));
        Assert.False(store.Current.IsAllowed("exe:9"));

        // Derselbe Stand noch einmal: keine Änderung
        Assert.False(store.Apply(Blocking("exe:9"), authoritative: true));
    }

    [Fact]
    public void AgentStore_RejectsImplausiblePolicies_AndResetsWhenUnpaired()
    {
        var store = new AgentPolicyStore(_dir.Path);
        var bad = AppPolicy.Default.WithCustomApp(new CustomApp("custom:1", "Böse", @"\\server\freigabe\x.bat"), T0);
        Assert.False(store.Apply(bad));

        store.Apply(Blocking("exe:1"));
        store.Reset();
        Assert.True(store.Current.IsAllowed("exe:1"));
        Assert.True(new AgentPolicyStore(_dir.Path).Current.IsAllowed("exe:1"));
    }

    [Fact]
    public void AdminStore_SavesPerDevice_AndTracksWhatTheDeviceApplied()
    {
        var device = Guid.NewGuid();
        var store = new PolicyStore(_dir.Path);
        var changes = 0;
        store.Changed += _ => changes++;

        Assert.True(store.IsApplied(device)); // nichts eingestellt, nichts zu übertragen
        var updated = store.Update(device, (p, now) => p.WithAllowed(["exe:1"], false, now));
        Assert.Equal(1, updated.Revision);
        Assert.False(store.IsApplied(device));

        store.MarkApplied(device, 1);
        Assert.True(store.IsApplied(device));
        Assert.Equal(2, changes);

        // Neu geöffnet (Panel-Neustart): alles noch da, anderer PC unberührt
        var reopened = new PolicyStore(_dir.Path);
        Assert.False(reopened.Get(device).IsAllowed("exe:1"));
        Assert.True(reopened.IsApplied(device));
        Assert.True(reopened.Get(Guid.NewGuid()).IsAllowed("exe:1"));

        reopened.Remove(device);
        Assert.Equal(0, reopened.Get(device).Revision);
    }

    private static readonly AppEntry[] Apps =
    [
        new("exe:discord", "Discord", AppSource.InstalledProgram, ExecutablePath: @"C:\D\Update.exe",
            LaunchArguments: "--processStart Discord.exe", IconHash: "icon-discord"),
        new("steam:427520", "Factorio", AppSource.Steam, SteamAppId: 427520, IconHash: "icon-f", CoverHash: "cover-f"),
        new("epic:fortnite", "Fortnite", AppSource.InstalledProgram, ExecutablePath: @"D:\Epic\Fortnite.exe",
            Launcher: GameLaunchers.Epic, LaunchUri: "com.epicgames.launcher://apps/fn?action=launch"),
        new("exe:vc", "Visual C++ Runtime", AppSource.InstalledProgram, ExecutablePath: @"C:\vc.exe", IsSystemComponent: true),
        new("prog:tool", "Tool ohne EXE", AppSource.InstalledProgram),
        new("store:calc", "Rechner", AppSource.StoreApp, StoreAppUserModelId: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
    ];

    private static AppImage? Image(string hash) => new(hash, "image/png", [1, 2, 3]);

    [Fact]
    public void Catalog_ShowsAllowedLaunchableApps_GamesFirst_NoSystemComponents()
    {
        var list = LauncherCatalog.Build(Apps, AppPolicy.Default, Image);

        Assert.Equal(["Factorio", "Fortnite", "Discord", "Rechner"], list.Apps.Select(a => a.Name));
        var fortnite = list.Apps.Single(a => a.Name == "Fortnite");
        Assert.StartsWith("com.epicgames.launcher://", fortnite.LaunchTarget);
        Assert.Null(fortnite.Arguments);
        Assert.Equal("--processStart Discord.exe", list.Apps.Single(a => a.Name == "Discord").Arguments);
        Assert.NotNull(list.Apps.Single(a => a.Name == "Factorio").Cover);
        Assert.Null(list.Apps.Single(a => a.Name == "Discord").Cover); // Cover nur bei Spielen
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", list.Apps.Single(a => a.Name == "Rechner").LaunchTarget);
    }

    [Fact]
    public void Catalog_HidesBlocked_AndHashChangesOnlyWithContent()
    {
        var all = LauncherCatalog.Build(Apps, AppPolicy.Default, Image);
        var again = LauncherCatalog.Build(Apps, AppPolicy.Default, Image);
        var blocked = LauncherCatalog.Build(Apps, Blocking("exe:discord"), Image);

        Assert.Equal(all.Hash, again.Hash);
        Assert.NotEqual(all.Hash, blocked.Hash);
        Assert.DoesNotContain(blocked.Apps, a => a.Name == "Discord");
    }

    /// <summary>
    /// Der Launcher fragt alle 2 s: Jede Abfrage muss sofort beantwortet werden, nicht nur die erste.
    /// Nur mit vollen Rechten des Servers wie im Dienst (SYSTEM): Im beschnittenen Testmodus darf das Konto keine
    /// weitere Instanz anlegen (dafür bräuchte es Schreibrecht, das dann auch der Client desselben Kontos hätte).
    /// </summary>
    [Fact]
    public async Task StatusPipe_AnswersEveryQueryQuickly()
    {
        const bool grantCurrentUser = true;
        var ct = TestContext.Current.CancellationToken;
        var pipeName = "MorniLAN.Test.Status." + Guid.NewGuid().ToString("N");
        var log = new ListLogger<LocalStatusServer>();
        using var server = new LocalStatusServer(() => LocalStatusPipe.Serialize(new AgentLocalStatus(AgentLinkState.Online,
                null, "Panel", "PC", "1.0", DateTimeOffset.UtcNow, "hash")),
            log, pipeName, grantCurrentUser);
        await server.StartAsync(ct);
        try
        {
            var problems = new List<string>();
            for (var i = 0; i < 25; i++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var status = await LocalStatusPipe.QueryAsync(TimeSpan.FromSeconds(1.5), ct, pipeName);
                if (status is null || watch.ElapsedMilliseconds > 500)
                    problems.Add($"#{i}: {(status is null ? "keine Antwort" : "langsam")} nach {watch.ElapsedMilliseconds} ms");
                await Task.Delay(50, ct);
            }
            Assert.True(problems.Count == 0, string.Join("; ", problems.Take(3)) + " | Log: " + string.Join("; ", log.Messages));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AppsPipe_DeliversLargeListToStandardUser()
    {
        // ~1,5 MB wie bei vielen Covern, und der Client hat nur die Rechte eines Standardbenutzers
        var big = Enumerable.Range(0, 30).Select(i => new LauncherApp($"steam:{i}", $"Spiel {i}", $"steam://rungameid/{i}",
            null, true, new byte[2000], new byte[50_000])).ToArray();
        var list = new LauncherAppList("hash-1", big);
        var pipeName = "MorniLAN.Test.Apps." + Guid.NewGuid().ToString("N");
        using var server = new LocalStatusServer(() => LauncherAppsPipe.Serialize(list),
            NullLogger<LocalStatusServer>.Instance, pipeName, grantCurrentUser: false);
        await server.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var received = await LauncherAppsPipe.QueryAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken, pipeName);

            Assert.NotNull(received);
            Assert.Equal("hash-1", received.Hash);
            Assert.Equal(30, received.Apps.Length);
            Assert.Equal(50_000, received.Apps[29].Cover!.Length);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }
}
