using Microsoft.Extensions.Logging.Abstractions;
using MorniLAN.Admin.Server;
using MorniLAN.Agent.Policy;
using MorniLAN.Agent.Restrictions;
using MorniLAN.Shared.Models;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Agent;

public sealed class RestrictionTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    // ───── Freigaben-Modell ─────

    [Fact]
    public void Restrictions_InPolicy_ToggleAccountsAndAreas()
    {
        var policy = AppPolicy.Default
            .WithRestrictedAccount("S-1-5-21-1", true, T0)
            .WithBlockedArea(WindowsAreas.Console, true, T0)
            .WithBlockedArea(WindowsAreas.Registry, true, T0)
            .WithBlockedArea(WindowsAreas.Registry, false, T0);

        Assert.True(policy.IsRestricted("S-1-5-21-1"));
        Assert.False(policy.IsRestricted("S-1-5-21-2"));
        Assert.Equal([WindowsAreas.Console], policy.BlockedAreas!);

        var off = policy.WithRestrictedAccount("S-1-5-21-1", false, T0);
        Assert.False(off.IsRestricted("S-1-5-21-1"));
    }

    [Fact]
    public void OldPolicyJson_WithoutRestrictionFields_RestrictsNothing()
    {
        const string old = """{"revision":1,"allowByDefault":true,"rules":[],"customApps":[],"updatedAt":"2026-10-06T05:00:00+00:00"}""";
        var policy = System.Text.Json.JsonSerializer.Deserialize(old, MorniLAN.Shared.Serialization.MorniLanJsonContext.Default.AppPolicy)!;
        Assert.False(policy.IsRestricted("S-1-5-21-1"));
        Assert.Null(policy.BlockedAreas);
    }

    // ───── Reine Logik ─────

    [Fact]
    public void BlockedExecutables_MapAreasToProgramNames()
    {
        var blocked = RestrictionPlan.BlockedExecutables([WindowsAreas.Console, WindowsAreas.TaskManager]);
        Assert.Contains("powershell.exe", blocked);
        Assert.Contains("cmd.exe", blocked);
        Assert.Contains("taskmgr.exe", blocked);
        Assert.DoesNotContain("regedit.exe", blocked);
        // msiexec bleibt außen vor (doppelt genutzt)
        Assert.DoesNotContain("msiexec.exe", RestrictionPlan.BlockedExecutables(WindowsAreas.All));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", true)]
    [InlineData(@"C:\Windows\System32\CMD.EXE", true)]
    [InlineData(@"C:\Spiele\spiel.exe", false)]
    [InlineData(null, false)]
    public void ShouldTerminate_MatchesByFileName(string? path, bool expected)
    {
        var blocked = RestrictionPlan.BlockedExecutables([WindowsAreas.Console]);
        Assert.Equal(expected, RestrictionPlan.ShouldTerminate(path, blocked));
    }

    [Fact]
    public void Administrators_AreNeverRestricted()
    {
        var policy = AppPolicy.Default.WithRestrictedAccount("S-1-5-21-admin", true, T0);
        Assert.False(RestrictionPlan.IsAccountRestricted(policy, "S-1-5-21-admin", isAdministrator: true));
        Assert.True(RestrictionPlan.IsAccountRestricted(policy, "S-1-5-21-admin", isAdministrator: false));
    }

    // ───── Prozess-Wächter ─────

    private RestrictionService NewService(AppPolicy policy, List<int> killed,
        IEnumerable<ProcessOwnership.RunningProcess> processes, LocalAccount[] accounts, bool paused = false,
        List<string>? writtenFor = null, List<string>? clearedFor = null)
    {
        var store = new AgentPolicyStore(_dir.Path);
        store.Apply(policy, authoritative: true);
        return new RestrictionService(store, NullLogger<RestrictionService>.Instance,
            writePolicies: (sid, _) => writtenFor?.Add(sid),
            clearPolicies: sid => clearedFor?.Add(sid),
            terminate: killed.Add,
            enumerate: () => processes,
            scanAccounts: () => accounts,
            isPaused: () => paused);
    }

    private static readonly LocalAccount Freund = new("S-1-5-21-100", "Freund", false);
    private static readonly LocalAccount Admin = new("S-1-5-21-1", "Chef", true);

    [Fact]
    public async Task Guard_KillsBlockedProgram_OnlyInRestrictedAccount()
    {
        var policy = AppPolicy.Default
            .WithRestrictedAccount(Freund.Sid, true, T0)
            .WithBlockedArea(WindowsAreas.Console, true, T0);
        var killed = new List<int>();
        var processes = new List<ProcessOwnership.RunningProcess>
        {
            new(1, @"C:\Windows\System32\cmd.exe", Freund.Sid), // gesperrt, eingeschränktes Konto → beenden
            new(2, @"C:\Windows\System32\cmd.exe", Admin.Sid), // Admin → in Ruhe lassen
            new(3, @"C:\Spiele\spiel.exe", Freund.Sid), // kein gesperrter Bereich → in Ruhe lassen
            new(4, @"C:\Windows\System32\powershell.exe", null), // Systemprozess ohne Konto → in Ruhe lassen
        };
        var written = new List<string>();
        var cleared = new List<string>();
        var service = NewService(policy, killed, processes, [Freund, Admin], writtenFor: written, clearedFor: cleared);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal([1], killed);
        Assert.Equal([Freund.Sid], written); // nur das eingeschränkte Konto bekommt Richtlinien
        Assert.Contains(Admin.Sid, cleared); // Admin wird aktiv freigeräumt
    }

    [Fact]
    public async Task Guard_DoesNothing_WhenPaused()
    {
        var policy = AppPolicy.Default
            .WithRestrictedAccount(Freund.Sid, true, T0)
            .WithBlockedArea(WindowsAreas.Console, true, T0);
        var killed = new List<int>();
        var cleared = new List<string>();
        var service = NewService(policy, killed,
            [new ProcessOwnership.RunningProcess(1, @"C:\Windows\System32\cmd.exe", Freund.Sid)],
            [Freund], paused: true, clearedFor: cleared);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(killed); // Notfall-Entsperrung: nichts beenden
        Assert.Contains(Freund.Sid, cleared); // und alle Richtlinien räumen
        Assert.False(service.State.Applied is false);
        Assert.Contains("Notfall", service.State.Message);
    }

    // ───── Panel-Ablage ─────

    [Fact]
    public void AdminAccountStore_KeepsValidAccounts_AndState()
    {
        var device = Guid.NewGuid();
        var store = new DeviceAccountStore(_dir.Path);
        store.Save(device, [Freund, Admin, new LocalAccount("kein-sid", "X", false)]);

        var reopened = new DeviceAccountStore(_dir.Path);
        Assert.Equal(["Freund", "Chef"], reopened.Get(device).Select(a => a.Name));

        store.SaveState(device, new RestrictionState(true, 1, "off", "alles gut"));
        Assert.Equal("alles gut", store.State(device).Message);
        store.Remove(device);
        Assert.Empty(new DeviceAccountStore(_dir.Path).Get(device));
    }
}
