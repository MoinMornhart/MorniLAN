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

    private RemoteAccessService NewService(bool sunshineInstalled = true, bool sunshineRuns = true) =>
        new(NullLogger<RemoteAccessService>.Instance,
            ensureSunshine: () => sunshineRuns,
            sunshineInstalled: () => sunshineInstalled,
            hostAddress: () => "192.168.178.50:47989",
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
    public void Unavailable_WhenSunshineMissing()
    {
        var service = NewService(sunshineInstalled: false);
        service.Start(allowWithoutConsent: true);
        Assert.Equal(RemoteSessionPhase.Unavailable, service.State.Phase);
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
