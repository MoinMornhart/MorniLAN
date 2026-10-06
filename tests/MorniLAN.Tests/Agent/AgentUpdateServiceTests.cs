using Microsoft.Extensions.Logging.Abstractions;
using MorniLAN.Agent.Updates;
using MorniLAN.Shared;
using MorniLAN.Shared.Updates;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Agent;

public sealed class AgentUpdateServiceTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Ein Release, das garantiert neuer ist als die laufende Testversion.</summary>
    private static IReadOnlyList<ReleaseInfo> NewerRelease()
    {
        var current = SemanticVersion.Parse(VersionInfo.Version);
        var newer = SemanticVersion.Parse($"{current.Major}.{current.Minor}.{current.Patch + 1}-beta.1");
        return
        [
            new ReleaseInfo($"v{newer}", newer, true, new Uri("https://example.invalid/r"), "",
            [
                new ReleaseAsset($"MorniLAN-Geraete-Setup-{newer}.exe", new Uri("https://example.invalid/g.exe"), 10, new string('a', 64)),
                new ReleaseAsset($"MorniLAN-Admin-Setup-{newer}.exe", new Uri("https://example.invalid/a.exe"), 10, new string('b', 64)),
            ]),
        ];
    }

    private sealed class Recorder
    {
        public List<string> Downloads { get; } = [];
        public List<string> Launches { get; } = [];
        public List<UpdatePhase> Phases { get; } = [];
    }

    private (AgentUpdateService Service, Recorder Calls) Create(IReadOnlyList<ReleaseInfo> releases, Func<bool> gameRunning,
        bool canInstall = true, Func<string>? runningVersion = null)
    {
        var calls = new Recorder();
        var service = new AgentUpdateService(NullLogger<AgentUpdateService>.Instance,
            _ => Task.FromResult(releases), gameRunning, canInstall,
            (update, _) =>
            {
                calls.Downloads.Add(update.Setup.Name);
                return Task.FromResult(@"C:\temp\" + update.Setup.Name);
            },
            (setup, _) => calls.Launches.Add(setup), _dir.Path, runningVersion);
        service.StateChanged += s => calls.Phases.Add(s.Phase);
        return (service, calls);
    }

    [Fact]
    public async Task NoNewerRelease_IsUpToDate()
    {
        var (service, calls) = Create([], () => false);
        Assert.False(await service.CheckAndInstallAsync(Ct));
        Assert.Equal(UpdatePhase.UpToDate, service.State.Phase);
        Assert.Empty(calls.Downloads);
    }

    [Fact]
    public async Task NewerRelease_IsDownloadedAndInstalled_WithDeviceSetup()
    {
        var (service, calls) = Create(NewerRelease(), () => false);
        Assert.False(await service.CheckAndInstallAsync(Ct));

        Assert.StartsWith("MorniLAN-Geraete-Setup-", Assert.Single(calls.Downloads));
        Assert.Single(calls.Launches);
        Assert.Equal(UpdatePhase.Installing, service.State.Phase);
        Assert.Equal([UpdatePhase.Checking, UpdatePhase.Downloading, UpdatePhase.Installing], calls.Phases);
    }

    [Fact]
    public async Task WhileGameRuns_NothingIsInstalled_AndItRetriesLater()
    {
        var (service, calls) = Create(NewerRelease(), () => true);
        Assert.True(await service.CheckAndInstallAsync(Ct)); // true = später erneut versuchen

        Assert.Empty(calls.Downloads);
        Assert.Empty(calls.Launches);
        Assert.Equal(UpdatePhase.WaitingForGame, service.State.Phase);
    }

    [Fact]
    public async Task GameStartsDuringDownload_DoesNotInstall()
    {
        var checks = 0;
        var (service, calls) = Create(NewerRelease(), () => checks++ > 0); // erst kein Spiel, nach dem Download schon
        Assert.True(await service.CheckAndInstallAsync(Ct));

        Assert.Single(calls.Downloads);
        Assert.Empty(calls.Launches);
        Assert.Equal(UpdatePhase.WaitingForGame, service.State.Phase);
    }

    [Fact]
    public async Task ConsoleMode_OnlyReportsUpdate()
    {
        var (service, calls) = Create(NewerRelease(), () => false, canInstall: false);
        await service.CheckAndInstallAsync(Ct);

        Assert.Equal(UpdatePhase.Available, service.State.Phase);
        Assert.NotNull(service.State.AvailableVersion);
        Assert.Empty(calls.Downloads);
    }

    [Fact]
    public async Task NetworkError_IsReported_NotThrown()
    {
        var service = new AgentUpdateService(NullLogger<AgentUpdateService>.Instance,
            _ => throw new HttpRequestException("kein Netz"), () => false, true,
            (_, _) => Task.FromResult(""), (_, _) => { }, _dir.Path);
        Assert.False(await service.CheckAndInstallAsync(Ct));
        Assert.Equal(UpdatePhase.Failed, service.State.Phase);
        Assert.Contains("kein Netz", service.State.Message);
    }

    [Fact]
    public async Task DownloadRefuses_WhenChecksumIsMissingOrWrong()
    {
        var release = NewerRelease()[0];
        var noDigest = new AvailableUpdate(release, release.Assets[0] with { Sha256 = null });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GitHubReleases.DownloadAsync(noDigest, Path.GetTempPath(), Ct));
    }

    // ───── Verifikation über den Neustart (Marker) ─────

    [Fact]
    public async Task Install_WritesPendingMarker_WithTargetVersion()
    {
        var (service, _) = Create(NewerRelease(), () => false);
        await service.CheckAndInstallAsync(Ct);

        var pending = UpdateMarker.Read(_dir.Path);
        Assert.NotNull(pending);
        Assert.Equal(service.State.AvailableVersion, pending.TargetVersion);
    }

    [Fact]
    public void Verify_AfterRestart_OnNewVersion_ReportsSuccess_AndClearsMarker()
    {
        UpdateMarker.Write(_dir.Path, "9.9.9", DateTimeOffset.UtcNow);
        var (service, _) = Create([], () => false, runningVersion: () => "9.9.9");

        service.VerifyPreviousUpdate();

        Assert.Equal(UpdatePhase.UpToDate, service.State.Phase);
        Assert.StartsWith("✓", service.State.Message);
        Assert.Null(UpdateMarker.Read(_dir.Path)); // Marker weg
    }

    [Fact]
    public void Verify_AfterRestart_StillOldVersion_ReportsFailure_AndClearsMarker()
    {
        UpdateMarker.Write(_dir.Path, "9.9.9", DateTimeOffset.UtcNow);
        var (service, _) = Create([], () => false, runningVersion: () => "0.1.0");

        service.VerifyPreviousUpdate();

        Assert.Equal(UpdatePhase.Failed, service.State.Phase);
        Assert.Contains("9.9.9", service.State.Message);
        Assert.Null(UpdateMarker.Read(_dir.Path));
    }

    [Fact]
    public void Verify_WithoutMarker_DoesNothing()
    {
        var (service, _) = Create([], () => false);
        service.VerifyPreviousUpdate();
        Assert.Equal(UpdatePhase.Unknown, service.State.Phase); // unverändert
    }
}
