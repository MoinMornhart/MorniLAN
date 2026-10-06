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
        Func<CancellationToken, Task<bool>>? installSunshine = null, Func<bool>? installedCheck = null,
        Func<string, string, CancellationToken, Task<bool>>? acceptPin = null) =>
        new(NullLogger<RemoteAccessService>.Instance,
            ensureSunshine: () => sunshineRuns,
            sunshineInstalled: installedCheck ?? (() => sunshineInstalled),
            hostAddress: () => "192.168.178.50:47989",
            // Standard im Test: NICHT wirklich winget aufrufen – als „fehlgeschlagen" behandeln
            installSunshine: installSunshine ?? (_ => Task.FromResult(false)),
            acceptPin: acceptPin ?? ((_, _, _) => Task.FromResult(true)),
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

    // ───── Automatische Kopplung per PIN ─────

    [Fact]
    public async Task Pair_WhileActive_AcceptsPin_AndReportsSuccess()
    {
        string? seenPin = null;
        var service = NewService(acceptPin: (pin, _, _) => { seenPin = pin; return Task.FromResult(true); });
        service.Start(allowWithoutConsent: true);

        var done = new TaskCompletionSource();
        service.StateChanged += s => { if (s.Message.Contains("Gekoppelt")) done.TrySetResult(); };
        service.Pair("1234");
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("1234", seenPin);
        Assert.Equal(RemoteSessionPhase.Active, service.State.Phase);
        Assert.Contains("Gekoppelt", service.State.Message);
    }

    [Fact]
    public async Task Pair_WhenRejected_ReportsRetryHint()
    {
        var service = NewService(acceptPin: (_, _, _) => Task.FromResult(false));
        service.Start(allowWithoutConsent: true);

        var done = new TaskCompletionSource();
        service.StateChanged += s => { if (s.Message.Contains("stimmte nicht")) done.TrySetResult(); };
        service.Pair("9999");
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("neue PIN", service.State.Message);
    }

    [Fact]
    public void Pair_WhenNotActive_IsIgnored()
    {
        var called = false;
        var service = NewService(acceptPin: (_, _, _) => { called = true; return Task.FromResult(true); });
        service.Pair("1234"); // keine Sitzung
        Assert.False(called);
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
        var configured = 0;
        var svc = new SunshineReadinessService(NullLogger<SunshineReadinessService>.Instance,
            isPaired: () => true, installed: () => installed, isGameRunning: () => false,
            install: _ => { calls++; installed = true; return Task.FromResult(true); }, canInstall: true,
            configure: () => configured++);

        Assert.True(await svc.TryEnsureAsync(CancellationToken.None));
        Assert.Equal(1, calls);
        Assert.Equal(1, configured); // nach der Installation die Zugangsdaten einrichten
    }

    [Fact]
    public async Task Readiness_SkipsWhenNotPaired_OrAlreadyInstalled_OrGameRunning()
    {
        var calls = 0;
        Func<CancellationToken, Task<bool>> inst = _ => { calls++; return Task.FromResult(true); };
        Action noConfigure = () => { };
        // nicht gekoppelt
        Assert.False(await new SunshineReadinessService(NullLogger<SunshineReadinessService>.Instance,
            isPaired: () => false, installed: () => false, isGameRunning: () => false, install: inst, canInstall: true, configure: noConfigure).TryEnsureAsync(default));
        // Spiel läuft
        Assert.False(await new SunshineReadinessService(NullLogger<SunshineReadinessService>.Instance,
            isPaired: () => true, installed: () => false, isGameRunning: () => true, install: inst, canInstall: true, configure: noConfigure).TryEnsureAsync(default));
        // schon installiert
        Assert.True(await new SunshineReadinessService(NullLogger<SunshineReadinessService>.Instance,
            isPaired: () => true, installed: () => true, isGameRunning: () => false, install: inst, canInstall: true, configure: noConfigure).TryEnsureAsync(default));
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

    // ───── Sunshine-Configurator (Zugangsdaten + PIN-API) ─────

    [Fact]
    public void Configurator_SetsCredentialsOnce_AndRestartsService()
    {
        var file = Path.Combine(_dir.Path, "sunshine.json");
        var credRuns = 0;
        var restarts = 0;
        var cfg = new SunshineConfigurator(file,
            runSunshine: (_, args) => { if (args is ["--creds", ..]) credRuns++; return 0; },
            afterCredentialsCreated: () => restarts++);

        var first = cfg.EnsureCredentials();
        var second = cfg.EnsureCredentials(); // beim zweiten Mal aus der Datei, kein erneutes --creds

        Assert.NotNull(first);
        Assert.Equal(first!.Password, second!.Password); // gleiche, gespeicherte Zugangsdaten
        Assert.Equal(1, credRuns);
        Assert.Equal(1, restarts); // Dienst nur einmal (beim Anlegen) neu gestartet
    }

    [Fact]
    public void Configurator_ReturnsNull_WhenCredsCommandFails()
    {
        var cfg = new SunshineConfigurator(Path.Combine(_dir.Path, "sunshine.json"),
            runSunshine: (_, _) => 1, afterCredentialsCreated: () => Assert.Fail("kein Neustart bei Fehler"));
        Assert.Null(cfg.EnsureCredentials());
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("0000", true)]
    [InlineData("123", false)]
    [InlineData("12345", false)]
    [InlineData("12a4", false)]
    [InlineData("", false)]
    public void Configurator_ValidatesPin(string pin, bool valid) =>
        Assert.Equal(valid, SunshineConfigurator.IsValidPin(pin));

    [Fact]
    public void Configurator_BuildsPinRequest_WithAuthAndBody()
    {
        var request = SunshineConfigurator.BuildPinRequest(
            new SunshineConfigurator.Credentials("mornilan", "secret"), "1234", "WohnzimmerPC");

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://localhost:{SunshineControl.ApiPort}/api/pin", request.RequestUri!.ToString());
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!));
        Assert.Equal("mornilan:secret", decoded);
        var body = request.Content!.ReadAsStringAsync().Result;
        Assert.Contains("\"pin\":\"1234\"", body);
        Assert.Contains("\"name\":\"WohnzimmerPC\"", body);
    }

    [Theory]
    [InlineData("{\"status\":\"true\"}", true)]
    [InlineData("{\"status\":true}", true)]
    [InlineData("{\"status\":\"false\"}", false)]
    [InlineData("{\"status\":false}", false)]
    [InlineData("kaputt", false)]
    public void Configurator_ParsesPinResult(string body, bool expected) =>
        Assert.Equal(expected, SunshineConfigurator.ParsePinAccepted(body));

    [Fact]
    public async Task Configurator_AcceptPin_SendsRequest_WhenCredsExist()
    {
        var file = Path.Combine(_dir.Path, "sunshine.json");
        var cfg = new SunshineConfigurator(file, runSunshine: (_, _) => 0, afterCredentialsCreated: () => { });
        cfg.EnsureCredentials(); // Zugangsdaten anlegen

        HttpRequestMessage? sent = null;
        var ok = await cfg.AcceptPinAsync("1234", "PC", (req, _) =>
        {
            sent = req;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"true\"}"),
            });
        }, CancellationToken.None);

        Assert.True(ok);
        Assert.NotNull(sent);
        Assert.EndsWith("/api/pin", sent!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Configurator_AcceptPin_RejectsInvalidPin_WithoutSending()
    {
        var cfg = new SunshineConfigurator(Path.Combine(_dir.Path, "sunshine.json"),
            runSunshine: (_, _) => 0, afterCredentialsCreated: () => { });
        var sent = false;
        var ok = await cfg.AcceptPinAsync("12", "PC", (_, _) => { sent = true; return Task.FromResult(new HttpResponseMessage()); },
            CancellationToken.None);
        Assert.False(ok);
        Assert.False(sent);
    }

    private sealed class FakeTime(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }
}
