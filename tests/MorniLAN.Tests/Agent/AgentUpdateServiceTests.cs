using Microsoft.Extensions.Logging.Abstractions;
using MorniLAN.Agent.Updates;
using MorniLAN.Shared;
using MorniLAN.Shared.Updates;

namespace MorniLAN.Tests.Agent;

public class AgentUpdateServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private static (AgentUpdateService Service, Recorder Calls) Create(IReadOnlyList<ReleaseInfo> releases, Func<bool> gameRunning,
        bool canInstall = true)
    {
        var calls = new Recorder();
        var service = new AgentUpdateService(NullLogger<AgentUpdateService>.Instance,
            _ => Task.FromResult(releases), gameRunning, canInstall,
            (update, _) =>
            {
                calls.Downloads.Add(update.Setup.Name);
                return Task.FromResult(@"C:\temp\" + update.Setup.Name);
            },
            (setup, _) => calls.Launches.Add(setup));
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
            (_, _) => Task.FromResult(""), (_, _) => { });
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
}
