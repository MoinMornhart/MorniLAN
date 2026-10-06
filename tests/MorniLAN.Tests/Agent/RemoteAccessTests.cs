using Microsoft.Extensions.Logging.Abstractions;
using MorniLAN.Admin.Server;
using MorniLAN.Agent.Remote;
using MorniLAN.Shared.Models;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Agent;

public sealed class RemoteAccessTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private DateTimeOffset _now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _dir.Dispose();

    private RemoteAccessService NewService(bool sunshineInstalled = true, bool sunshineRuns = true,
        Func<CancellationToken, Task<bool>>? installSunshine = null, Func<bool>? installedCheck = null) =>
        new(NullLogger<RemoteAccessService>.Instance,
            ensureSunshine: () => sunshineRuns,
            sunshineInstalled: installedCheck ?? (() => sunshineInstalled),
            hostAddress: () => "192.168.178.50:47989",
            // Standard im Test: NICHT wirklich winget aufrufen – als „fehlgeschlagen" behandeln
            installSunshine: installSunshine ?? (_ => Task.FromResult(false)),
            time: new FakeTime(() => _now));

    [Fact]
    public void Start_WithoutConsent_GoesActive_WithHost()
    {
        var service = NewService();
        service.Start(allowWithoutConsent: true);

        Assert.Equal(RemoteSessionPhase.Active, service.State.Phase);
        Assert.Equal("192.168.178.50:47989", service.State.Host);
    }

    [Fact]
    public void Start_WithConsent_AsksFirst_ThenActivatesOnAllow()
    {
        var service = NewService();
        service.Start(allowWithoutConsent: false);
        Assert.Equal(RemoteSessionPhase.WaitingForConsent, service.State.Phase);

        service.Consent(allow: true);
        Assert.Equal(RemoteSessionPhase.Active, service.State.Phase);
    }

    [Fact]
    public void Consent_Deny_EndsAsDenied()
    {
        var service = NewService();
        service.Start(allowWithoutConsent: false);
        service.Consent(allow: false);
        Assert.Equal(RemoteSessionPhase.Denied, service.State.Phase);
    }

    [Fact]
    public void ConsentTimeout_DropsRequest()
    {
        var service = NewService();
        service.Start(allowWithoutConsent: false);
        _now = _now.AddSeconds(61);
        service.Tick();
        Assert.Equal(RemoteSessionPhase.Denied, service.State.Phase);
        Assert.Contains("Keine Antwort", service.State.Message);
    }

    [Fact]
    public void Unavailable_WhenSunshineMissing_AndAutoInstallFails()
    {
        var service = NewService(sunshineInstalled: false); // Install schlägt im Test fehl (kein echtes winget)
        service.Start(allowWithoutConsent: true);
        Assert.Equal(RemoteSessionPhase.Unavailable, service.State.Phase);
        Assert.Contains("einrichten", service.State.Message);
    }

    [Fact]
    public void MissingSunshine_IsAutoInstalled_ThenActivates()
    {
        var installed = false;
        var installCalls = 0;
        var service = NewService(
            installedCheck: () => installed,
            installSunshine: _ => { installCalls++; installed = true; return Task.FromResult(true); });

        service.Start(allowWithoutConsent: true);

        Assert.Equal(1, installCalls);                         // einmal automatisch installiert
        Assert.Equal(RemoteSessionPhase.Active, service.State.Phase); // und danach direkt aktiv
        Assert.Equal("192.168.178.50:47989", service.State.Host);
    }

    [Fact]
    public void InputLock_OnlyWhileActive()
    {
        var service = NewService();
        service.SetInputLock(true);
        Assert.False(service.State.InputLocked); // keine Sitzung → wirkungslos

        service.Start(allowWithoutConsent: true);
        service.SetInputLock(true);
        Assert.True(service.State.InputLocked);

        service.Stop();
        Assert.Equal(RemoteSessionPhase.Idle, service.State.Phase);
        Assert.False(service.State.InputLocked);
    }

    [Fact]
    public void LateConsent_AfterStop_DoesNothing()
    {
        var service = NewService();
        service.Start(allowWithoutConsent: false);
        service.Stop();
        service.Consent(allow: true);
        Assert.Equal(RemoteSessionPhase.Idle, service.State.Phase);
    }

    [Fact]
    public async Task SunshineInstaller_DownloadsMsi_AndRunsIt()
    {
        string? ran = null;
        var ok = await SunshineInstaller.InstallAsync(
            download: _ => Task.FromResult<string?>(@"C:\temp\Sunshine-Windows-AMD64-installer.msi"),
            run: (path, _) => { ran = path; return Task.FromResult(0); }, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(@"C:\temp\Sunshine-Windows-AMD64-installer.msi", ran);
    }

    [Fact]
    public async Task SunshineInstaller_FailsWhenDownloadFails()
    {
        var ran = false;
        var ok = await SunshineInstaller.InstallAsync(
            download: _ => Task.FromResult<string?>(null), run: (_, _) => { ran = true; return Task.FromResult(0); },
            CancellationToken.None);

        Assert.False(ok);
        Assert.False(ran); // ohne Datei kein msiexec
    }

    [Theory]
    [InlineData(0, true)]      // Erfolg
    [InlineData(3010, true)]   // Erfolg, Neustart nötig
    [InlineData(1, false)]     // Fehler
    public async Task SunshineInstaller_InterpretsExitCode(int code, bool expected)
    {
        var ok = await SunshineInstaller.InstallAsync(
            download: _ => Task.FromResult<string?>("x.msi"), run: (_, _) => Task.FromResult(code), CancellationToken.None);
        Assert.Equal(expected, ok);
    }

    // ───── Sunshine automatisch einrichten (proaktiv) ─────

    [Fact]
    public async Task Readiness_InstallsWhenPaired_AndNotYetInstalled()
    {
        var installed = false;
        var calls = 0;
        var svc = new SunshineReadinessService(NullLogger<SunshineReadinessService>.Instance,
            isPaired: () => true, installed: () => installed, isGameRunning: () => false,
            install: _ => { calls++; installed = true; return Task.FromResult(true); }, canInstall: true);

        Assert.True(await svc.TryEnsureAsync(CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Readiness_SkipsWhenNotPaired_OrAlreadyInstalled_OrGameRunning()
    {
        var calls = 0;
        Func<CancellationToken, Task<bool>> inst = _ => { calls++; return Task.FromResult(true); };
        // nicht gekoppelt
        Assert.False(await new SunshineReadinessService(NullLogger<SunshineReadinessService>.Instance,
            isPaired: () => false, installed: () => false, isGameRunning: () => false, install: inst, canInstall: true).TryEnsureAsync(default));
        // Spiel läuft
        Assert.False(await new SunshineReadinessService(NullLogger<SunshineReadinessService>.Instance,
            isPaired: () => true, installed: () => false, isGameRunning: () => true, install: inst, canInstall: true).TryEnsureAsync(default));
        // schon installiert
        Assert.True(await new SunshineReadinessService(NullLogger<SunshineReadinessService>.Instance,
            isPaired: () => true, installed: () => true, isGameRunning: () => false, install: inst, canInstall: true).TryEnsureAsync(default));
        Assert.Equal(0, calls); // in keinem Fall installiert
    }

    // ───── Panel-Ablage ─────

    [Fact]
    public void AdminRemoteStore_PersistsSettings_AndKeepsStateInMemory()
    {
        var device = Guid.NewGuid();
        var store = new RemoteAccessStore(_dir.Path);
        Assert.False(store.Settings(device).AllowWithoutConsent);

        store.SetAllowWithoutConsent(device, true);
        Assert.True(new RemoteAccessStore(_dir.Path).Settings(device).AllowWithoutConsent);

        store.Save(device, new RemoteSessionState(RemoteSessionPhase.Active, true, "h:1", "läuft", _now));
        Assert.Equal(RemoteSessionPhase.Active, store.State(device).Phase);

        store.Remove(device);
        Assert.False(new RemoteAccessStore(_dir.Path).Settings(device).AllowWithoutConsent);
    }

    private sealed class FakeTime(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }
}
