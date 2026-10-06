using System.Diagnostics;

namespace MorniLAN.Agent.Remote;

/// <summary>
/// Richtet Sunshine (den Streaming-Host) bei Bedarf automatisch ein – über winget, das der Agent (als SYSTEM) ohnehin
/// für Installationen nutzt. So muss der Admin Sunshine nicht von Hand installieren; verlangt er den Fernzugriff und
/// Sunshine fehlt, holt der Agent es selbst. Das eigentliche Streaming testen wir zu zweit.
/// </summary>
internal static class SunshineInstaller
{
    /// <summary>winget-Paket-ID von Sunshine (LizardByte).</summary>
    public const string WingetId = "LizardByte.Sunshine";

    /// <summary>Installiert Sunshine still per winget. true bei Erfolg (Exitcode 0).</summary>
    public static Task<bool> InstallAsync(CancellationToken cancellationToken) => InstallAsync(RunWinget, cancellationToken);

    internal static async Task<bool> InstallAsync(Func<string, CancellationToken, Task<int>> runWinget,
        CancellationToken cancellationToken)
    {
        var args = $"install --id {WingetId} --exact --silent --accept-package-agreements --accept-source-agreements";
        var code = await runWinget(args, cancellationToken);
        return code == 0;
    }

    private static async Task<int> RunWinget(string arguments, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo("winget", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (process is null)
            return -1;

        // Die Ausgabe nebenher leeren, sonst blockiert winget beim Schreiben (voller Pipe-Puffer) → Deadlock.
        var drain = Task.WhenAll(
            process.StandardOutput.ReadToEndAsync(cancellationToken),
            process.StandardError.ReadToEndAsync(cancellationToken));

        // Eigenes Timeout: Installationen hängen sonst unbegrenzt (Aufrufer gibt kein Token mit).
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return -1;
        }
        try { await drain; } catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        return process.ExitCode;
    }
}
