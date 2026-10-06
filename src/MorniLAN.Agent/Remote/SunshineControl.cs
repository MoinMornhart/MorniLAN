using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace MorniLAN.Agent.Remote;

/// <summary>
/// Startet und prüft Sunshine (den Streaming-Host). Sunshine läuft als eigener Windows-Dienst; dieses hier sorgt nur
/// dafür, dass es läuft. Die Installation übernimmt <see cref="SunshineInstaller"/> automatisch per winget, sobald der
/// Fernzugriff verlangt wird und Sunshine fehlt.
/// </summary>
internal static class SunshineControl
{
    private const string ServiceName = "SunshineService";

    /// <summary>Standard-Port, auf dem Moonlight Sunshine findet (HTTPS-Kopplung).</summary>
    public const int MoonlightPort = 47989;

    public static bool IsInstalled() => ServiceExists() || FindExecutable() is not null;

    /// <summary>Stellt sicher, dass Sunshine läuft. true, wenn es (jetzt) läuft.</summary>
    public static bool EnsureRunning()
    {
        if (ServiceExists())
        {
            try
            {
                using var service = new ServiceController(ServiceName);
                if (service.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
                    service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                return service.Status == ServiceControllerStatus.Running;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
            {
                return false;
            }
        }
        // Kein Dienst: Sunshine als normale Anwendung starten (falls vorhanden)
        if (FindExecutable() is { } exe && !IsProcessRunning())
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return false;
            }
        }
        return IsProcessRunning();
    }

    private static bool ServiceExists() =>
        ServiceController.GetServices().Any(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase));

    private static bool IsProcessRunning() => Process.GetProcessesByName("sunshine").Length > 0;

    private static string? FindExecutable()
    {
        foreach (var keyPath in new[] { @"SOFTWARE\LizardByte\Sunshine", @"SOFTWARE\WOW6432Node\LizardByte\Sunshine" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue("InstallPath") is string path)
            {
                var exe = Path.Combine(path, "sunshine.exe");
                if (File.Exists(exe))
                    return exe;
            }
        }
        foreach (var program in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            var exe = Path.Combine(program, "Sunshine", "sunshine.exe");
            if (File.Exists(exe))
                return exe;
        }
        return null;
    }
}
