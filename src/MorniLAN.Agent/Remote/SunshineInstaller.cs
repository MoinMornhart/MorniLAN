using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using MorniLAN.Shared;

namespace MorniLAN.Agent.Remote;

/// <summary>
/// Richtet Sunshine (den Streaming-Host) bei Bedarf automatisch ein: lädt die offizielle Windows-MSI von LizardByte
/// direkt herunter und installiert sie still per <c>msiexec</c>. Bewusst NICHT über winget – das ist im Dienst-Kontext
/// (SYSTEM) nicht verfügbar; msiexec läuft als SYSTEM problemlos. Das eigentliche Streaming testen wir zu zweit.
/// </summary>
internal static class SunshineInstaller
{
    private const string Repo = "LizardByte/Sunshine";

    private static readonly HttpClient Http = CreateClient();

    public static Task<bool> InstallAsync(CancellationToken cancellationToken) =>
        InstallAsync(DownloadLatestMsiAsync, RunMsiexecAsync, cancellationToken);

    /// <summary>download: lädt die MSI und gibt den Pfad zurück (oder null). run: msiexec, gibt den Exitcode zurück.</summary>
    internal static async Task<bool> InstallAsync(Func<CancellationToken, Task<string?>> download,
        Func<string, CancellationToken, Task<int>> run, CancellationToken cancellationToken)
    {
        var msi = await download(cancellationToken);
        if (msi is null)
            return false;
        var code = await run(msi, cancellationToken);
        // 0 = Erfolg, 3010 = Erfolg, Neustart nötig (für Sunshine nicht zwingend sofort)
        return code is 0 or 3010;
    }

    /// <summary>Neueste Sunshine-MSI passend zur Architektur herunterladen.</summary>
    private static async Task<string?> DownloadLatestMsiAsync(CancellationToken cancellationToken)
    {
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "ARM64" : "AMD64";
        var assetName = $"Sunshine-Windows-{arch}-installer.msi";
        try
        {
            await using var stream = await Http.GetStreamAsync(
                $"https://api.github.com/repos/{Repo}/releases/latest", cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            string? url = null;
            foreach (var asset in json.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name)
                    && string.Equals(name.GetString(), assetName, StringComparison.OrdinalIgnoreCase)
                    && asset.TryGetProperty("browser_download_url", out var downloadUrl))
                {
                    url = downloadUrl.GetString();
                    break;
                }
            }
            if (url is null)
                return null;

            var target = Path.Combine(AgentPaths.Data, "updates", assetName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp = target + ".download";
            await using (var output = File.Create(temp))
            await using (var input = await Http.GetStreamAsync(url, cancellationToken))
                await input.CopyToAsync(output, cancellationToken);
            File.Move(temp, target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    private static async Task<int> RunMsiexecAsync(string msiPath, CancellationToken cancellationToken)
    {
        var log = Path.Combine(AgentPaths.Logs, "sunshine-install.log");
        using var process = Process.Start(new ProcessStartInfo("msiexec",
            $"/i \"{msiPath}\" /quiet /norestart /L*v \"{log}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (process is null)
            return -1;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return -1;
        }
        return process.ExitCode;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MorniLAN", VersionInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
