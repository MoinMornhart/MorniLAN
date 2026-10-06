using System.Diagnostics;

namespace MorniLAN.Agent.Restrictions;

/// <summary>
/// App Control für Unternehmen (WDAC): sperrt auf Windows-Ebene nicht freigegebene Programme. Gilt für den ganzen
/// PC, also auch fürs Admin-Konto, und ein Fehler kann Windows lahmlegen – deshalb wird hier nur sicher
/// <see cref="Disable"/> umgesetzt. Der scharfe Betrieb kommt als eigener Schritt mit Prüfmodus zuerst
/// (erst protokollieren, dann blockieren) und braucht die ausdrückliche Freigabe des Admins.
/// </summary>
internal static class AppControl
{
    /// <summary>Entfernt eine evtl. vorhandene MorniLAN-Richtlinie. Immer gefahrlos, auch wenn keine da ist.</summary>
    public static void Disable()
    {
        // Richtliniendatei entfernen, falls vorhanden; CiTool aktualisiert beim nächsten Neustart.
        foreach (var file in PolicyFiles())
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        TryCiTool("--remove-policy", PolicyId);
    }

    public static string CurrentMode() => PolicyFiles().Any() ? "audit" : "off";

    private const string PolicyId = "{a1b2c3d4-0000-4000-8000-morni1an0000}";

    private static IEnumerable<string> PolicyFiles()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "CodeIntegrity", "CiPolicies", "Active");
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*MorniLAN*.cip") : [];
    }

    private static void TryCiTool(params string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("CiTool.exe")
            {
                Arguments = string.Join(' ', arguments),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(15000);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // CiTool gibt es erst ab Windows 11; auf älteren Systemen entfällt App Control ohnehin.
        }
    }
}
