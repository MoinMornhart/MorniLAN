using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Restrictions;

/// <summary>
/// Übersetzt die Freigaben in konkrete Sperren für die eingeschränkten Konten. Reine Logik, damit sie ohne
/// echtes Windows prüfbar ist. Welche Programme der Prozess-Wächter beendet, hängt nur von den gesperrten
/// Windows-Bereichen ab; einzelne Spiele/Programme sperrt App Control (nicht der Wächter, das wäre zu grob).
/// </summary>
internal static class RestrictionPlan
{
    /// <summary>
    /// Programme (Dateiname, klein), die in einer eingeschränkten Sitzung nicht laufen dürfen.
    /// PowerShell und Windows Terminal stehen hier, weil es dafür keine saubere Benutzer-Richtlinie gibt;
    /// die übrigen deckt zusätzlich die Registry-Richtlinie ab (Gürtel und Hosenträger).
    /// msiexec fehlt bewusst: doppelt genutzt (auch Deinstallation/Reparatur), das Installieren sperrt die Richtlinie.
    /// </summary>
    public static IReadOnlySet<string> BlockedExecutables(IEnumerable<string> blockedAreas)
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var area in blockedAreas)
        {
            foreach (var exe in area switch
            {
                WindowsAreas.Console => new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "powershell_ise.exe", "wt.exe" },
                WindowsAreas.Registry => ["regedit.exe", "reg.exe"],
                WindowsAreas.TaskManager => ["taskmgr.exe"],
                WindowsAreas.Settings => ["systemsettings.exe", "control.exe"],
                _ => Array.Empty<string>(),
            })
                blocked.Add(exe);
        }
        return blocked;
    }

    /// <summary>
    /// Soll dieser laufende Prozess beendet werden? Nur in eingeschränkten Konten, nie das eigene Admin-Konto,
    /// und nie ein Programm aus dem Windows-Ordner, das nicht ausdrücklich auf der Sperrliste steht
    /// (damit der Wächter nicht aus Versehen Windows lahmlegt).
    /// </summary>
    public static bool ShouldTerminate(string? executablePath, IReadOnlySet<string> blockedExecutables)
    {
        if (string.IsNullOrEmpty(executablePath))
            return false;
        var file = Path.GetFileName(executablePath);
        return blockedExecutables.Contains(file);
    }

    /// <summary>Gilt für dieses Konto überhaupt eine Einschränkung? (Nie für Administratoren.)</summary>
    public static bool IsAccountRestricted(AppPolicy policy, string sid, bool isAdministrator) =>
        !isAdministrator && policy.IsRestricted(sid);

    /// <summary>
    /// Soll in diesem Konto der Launcher der Desktop sein (Shell-Ersatz)? Nie für Administratoren – das eigene
    /// Admin-Konto behält immer den normalen Windows-Desktop.
    /// </summary>
    public static bool IsAccountKiosk(AppPolicy policy, string sid, bool isAdministrator) =>
        !isAdministrator && policy.IsKiosk(sid);
}
