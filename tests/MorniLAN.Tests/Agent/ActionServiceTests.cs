using Microsoft.Extensions.Logging.Abstractions;
using MorniLAN.Admin.Platform;
using MorniLAN.Agent.Actions;
using MorniLAN.Shared.Models;

namespace MorniLAN.Tests.Agent;

public class ActionServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ActionService New(List<(string File, string Args)> calls, int code = 0, string output = "",
        Func<string, string, CancellationToken, Task<string>>? download = null, List<AdminMessage>? messages = null) =>
        new(NullLogger<ActionService>.Instance,
            runProcess: (file, args, _) => { calls.Add((file, args)); return (code, output); },
            download: download ?? ((_, ext, _) => Task.FromResult($"C:\\temp\\x{ext}")),
            showMessage: messages is null ? null : messages.Add);

    [Fact]
    public async Task Install_UsesWingetSilently()
    {
        var calls = new List<(string, string)>();
        var result = await New(calls).RunAsync(new InstallPackageCommand("Valve.Steam", "Steam"), Ct);

        Assert.True(result.Success);
        var (file, args) = Assert.Single(calls);
        Assert.Equal("winget", file);
        Assert.Contains("install --id Valve.Steam --exact --silent", args);
        Assert.Contains("--accept-package-agreements", args);
    }

    [Fact]
    public async Task Uninstall_UsesWinget_WithoutPackageAgreements()
    {
        var calls = new List<(string, string)>();
        await New(calls).RunAsync(new UninstallPackageCommand("Valve.Steam"), Ct);
        Assert.Contains("uninstall --id Valve.Steam", calls[0].Item2);
        Assert.DoesNotContain("--accept-package-agreements", calls[0].Item2);
    }

    [Fact]
    public async Task Install_RejectsBadPackageId()
    {
        var calls = new List<(string, string)>();
        var result = await New(calls).RunAsync(new InstallPackageCommand("böse id; rm -rf"), Ct);
        Assert.False(result.Success);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("http://example.com/x.exe", "https")]
    [InlineData("https://example.com/x.zip", ".exe")]
    public async Task InstallFromUrl_Rejects_NonHttpsOrWrongType(string url, string reasonContains)
    {
        var result = await New([]).RunAsync(new InstallFromUrlCommand(url, "X"), Ct);
        Assert.False(result.Success);
        Assert.Contains(reasonContains, result.Message);
    }

    [Fact]
    public async Task InstallFromUrl_Exe_DownloadsAndRunsSilently()
    {
        var calls = new List<(string, string)>();
        var downloaded = "";
        var service = New(calls, download: (url, ext, _) => { downloaded = url; return Task.FromResult($"C:\\temp\\setup{ext}"); });
        var result = await service.RunAsync(new InstallFromUrlCommand("https://example.com/setup.exe", "Beispiel"), Ct);

        Assert.True(result.Success);
        Assert.Equal("https://example.com/setup.exe", downloaded);
        Assert.Equal("C:\\temp\\setup.exe", calls[0].Item1);
        Assert.Contains("/S", calls[0].Item2);
    }

    [Fact]
    public async Task InstallFromUrl_Msi_UsesMsiexec()
    {
        var calls = new List<(string, string)>();
        await New(calls).RunAsync(new InstallFromUrlCommand("https://example.com/app.msi", "App"), Ct);
        Assert.Equal("msiexec", calls[0].Item1);
        Assert.Contains("/qn", calls[0].Item2);
    }

    [Fact]
    public async Task Message_IsStoredAndRaised()
    {
        var messages = new List<AdminMessage>();
        var service = New([], messages: messages);
        var result = await service.RunAsync(new ShowMessageCommand("Hallo", "Gleich Abendessen"), Ct);

        Assert.True(result.Success);
        Assert.Equal("Gleich Abendessen", Assert.Single(messages).Text);
        Assert.Equal("Hallo", service.CurrentMessage!.Title);
    }

    [Fact]
    public async Task Restart_UsesShutdownCommand()
    {
        var calls = new List<(string, string)>();
        await New(calls).RunAsync(new RestartCommand(DelaySeconds: 30, Shutdown: false), Ct);
        Assert.Equal("shutdown", calls[0].Item1);
        Assert.Contains("/r /t 30", calls[0].Item2);
    }

    [Fact]
    public async Task FailingProcess_ReportsError()
    {
        var result = await New([], code: 1, output: "Paket nicht gefunden").RunAsync(new InstallPackageCommand("X.Y"), Ct);
        Assert.False(result.Success);
        Assert.Contains("Paket nicht gefunden", result.Message);
    }

    // ───── winget-Ausgabe zerlegen ─────

    [Fact]
    public void WingetParse_ExtractsNameAndId()
    {
        // Spaltengenau wie die echte winget-Ausgabe (feste Breiten)
        string Row(string name, string id, string version) => name.PadRight(22) + id.PadRight(28) + version.PadRight(12) + "winget";
        var output = string.Join('\n',
            Row("Name", "Id", "Version"),
            new string('-', 70),
            Row("Discord", "Discord.Discord", "1.0.9035"),
            Row("Steam", "Valve.Steam", "2.10"));

        var hits = WingetSearch.Parse(output);
        Assert.Contains(hits, h => h is { Name: "Discord", Id: "Discord.Discord" });
        Assert.Contains(hits, h => h.Id == "Valve.Steam");
    }

    [Fact]
    public void WingetParse_EmptyOnGarbage() => Assert.Empty(WingetSearch.Parse("kein winget hier"));
}
