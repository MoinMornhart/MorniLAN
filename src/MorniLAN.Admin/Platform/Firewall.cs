using System.ComponentModel;
using System.Diagnostics;
using MorniLAN.Shared;

namespace MorniLAN.Admin.Platform;

/// <summary>
/// Windows-Firewall-Regeln des Admin-Panels. Gleiche Namen wie in tools/firewall.ps1.
/// Gilt für jedes Netzwerkprofil, aber nur für Geräte aus dem eigenen Subnetz bzw. aus Tailscale.
/// </summary>
internal static class Firewall
{
    public const string SetupArgument = "--setup-firewall";

    public sealed record Rule(string Name, string Protocol, int Port, string RemoteIp, string Purpose);

    public static readonly IReadOnlyList<Rule> AdminRules =
    [
        new("MorniLAN Admin (LAN)", "TCP", MorniLanConstants.AdminPort, "localsubnet", "Verbindungen der PCs im Heimnetz"),
        new("MorniLAN Admin (Tailscale)", "TCP", MorniLanConstants.AdminPort, "100.64.0.0/10", "Verbindungen über Tailscale"),
        new("MorniLAN Admin Suche (LAN)", "UDP", MorniLanConstants.DiscoveryPort, "localsubnet", "Suchanfragen der PCs im Heimnetz"),
    ];

    /// <summary>Prüft ohne Admin-Rechte, ob eine Regel existiert.</summary>
    public static bool Exists(Rule rule) => Netsh($"advfirewall firewall show rule name=\"{rule.Name}\"") == 0;

    public static IReadOnlyList<(Rule Rule, bool Present)> Check() => [.. AdminRules.Select(r => (r, Exists(r)))];

    /// <summary>Legt alle Regeln neu an. Braucht Admin-Rechte (läuft im per UAC gestarteten Hilfsprozess).</summary>
    public static int Apply()
    {
        var failures = 0;
        foreach (var rule in AdminRules)
        {
            Netsh($"advfirewall firewall delete rule name=\"{rule.Name}\"");
            var code = Netsh($"advfirewall firewall add rule name=\"{rule.Name}\" dir=in action=allow " +
                             $"protocol={rule.Protocol} localport={rule.Port} remoteip={rule.RemoteIp} profile=any " +
                             "description=\"MorniLAN Admin-Panel\"");
            if (code != 0)
                failures++;
        }
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Startet das Panel selbst mit Admin-Rechten (eine UAC-Abfrage) nur zum Einrichten der Regeln.
    /// false, wenn die Abfrage abgelehnt wurde oder etwas schiefging.
    /// </summary>
    public static async Task<bool> SetupElevatedAsync()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, SetupArgument)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false; // UAC-Abfrage abgelehnt
        }
    }

    private static int Netsh(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("netsh.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return -1;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(15_000);
            return process.HasExited ? process.ExitCode : -1;
        }
        catch (Win32Exception)
        {
            return -1;
        }
    }
}
