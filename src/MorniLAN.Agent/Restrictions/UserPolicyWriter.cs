using System.Diagnostics;
using Microsoft.Win32;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Restrictions;

/// <summary>
/// Schreibt die Windows-Benutzer-Richtlinien eines eingeschränkten Kontos in dessen Registry-Zweig (HKEY_USERS\&lt;SID&gt;).
/// Ist das Konto nicht angemeldet, lädt der Dienst (SYSTEM) kurz dessen NTUSER.DAT. Alle Werte, die MorniLAN setzt,
/// lassen sich mit <see cref="Clear"/> restlos entfernen – nichts bleibt heimlich zurück.
/// </summary>
internal static class UserPolicyWriter
{
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string System = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string CmdPolicy = @"Software\Policies\Microsoft\Windows\System";
    private const string StorePolicy = @"Software\Policies\Microsoft\WindowsStore";
    private const string Winlogon = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";

    /// <summary>Setzt genau die Richtlinien der gesperrten Bereiche; nicht gesperrte Bereiche werden entfernt.</summary>
    public static void Apply(string sid, IReadOnlySet<string> blockedAreas)
    {
        WithUserHive(sid, root =>
        {
            Set(root, Explorer, "NoControlPanel", blockedAreas.Contains(WindowsAreas.Settings));
            Set(root, System, "DisableRegistryTools", blockedAreas.Contains(WindowsAreas.Registry));
            Set(root, System, "DisableTaskMgr", blockedAreas.Contains(WindowsAreas.TaskManager));
            // 2 = Eingabeaufforderung aus, Batch-Skripte (z. B. von Spiele-Startern) laufen weiter
            Set(root, CmdPolicy, "DisableCMD", blockedAreas.Contains(WindowsAreas.Console), onValue: 2);
            Set(root, StorePolicy, "RemoveWindowsStore", blockedAreas.Contains(WindowsAreas.Install));
        });
    }

    /// <summary>Entfernt alle von MorniLAN gesetzten Richtlinien (Konto nicht mehr eingeschränkt, Notfall-Entsperrung).</summary>
    public static void Clear(string sid) => Apply(sid, new HashSet<string>());

    /// <summary>
    /// Macht den Launcher zum Desktop des Kontos: setzt die benutzereigene Shell (Winlogon\Shell). Windows nimmt
    /// diesen Wert statt explorer.exe – kein Startmenü, keine Taskleiste. <paramref name="shellCommand"/> null
    /// entfernt den Wert wieder, dann startet beim nächsten Anmelden wieder der normale Windows-Desktop.
    /// Wirkt bei der nächsten Anmeldung des Kontos (eine laufende Sitzung bleibt, bis man sich neu anmeldet).
    /// </summary>
    public static void SetShell(string sid, string? shellCommand) =>
        WithUserHive(sid, root =>
        {
            if (string.IsNullOrWhiteSpace(shellCommand))
            {
                using var key = root.OpenSubKey(Winlogon, writable: true);
                key?.DeleteValue("Shell", throwOnMissingValue: false);
            }
            else
            {
                using var key = root.CreateSubKey(Winlogon);
                key.SetValue("Shell", shellCommand, RegistryValueKind.String);
            }
        });

    /// <summary>Stellt in diesem Konto wieder den normalen Windows-Desktop her (explorer.exe).</summary>
    public static void ClearShell(string sid) => SetShell(sid, null);

    private static void Set(RegistryKey root, string path, string name, bool on, int onValue = 1)
    {
        if (on)
        {
            using var key = root.CreateSubKey(path);
            key.SetValue(name, onValue, RegistryValueKind.DWord);
        }
        else
        {
            using var key = root.OpenSubKey(path, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    /// <summary>Öffnet den Registry-Zweig des Kontos (angemeldet: direkt; sonst NTUSER.DAT laden und wieder entladen).</summary>
    private static void WithUserHive(string sid, Action<RegistryKey> work)
    {
        using (var loaded = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default).OpenSubKey(sid, writable: true))
        {
            if (loaded is not null)
            {
                work(loaded);
                return;
            }
        }

        var profilePath = ProfilePath(sid) ?? throw new InvalidOperationException($"Kein Profilpfad für {sid}");
        var hiveFile = Path.Combine(profilePath, "NTUSER.DAT");
        var mountName = "MorniLAN_" + sid.Replace('-', '_');
        if (Reg("load", $@"HKU\{mountName}", hiveFile) != 0)
            throw new InvalidOperationException($"NTUSER.DAT von {sid} nicht ladbar (Konto evtl. aktiv)");
        try
        {
            using var mounted = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default).OpenSubKey(mountName, writable: true);
            if (mounted is not null)
                work(mounted);
        }
        finally
        {
            GC.Collect(); // offene Schlüssel freigeben, sonst schlägt das Entladen fehl
            GC.WaitForPendingFinalizers();
            Reg("unload", $@"HKU\{mountName}");
        }
    }

    private static string? ProfilePath(string sid)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        return key?.GetValue("ProfileImagePath") as string;
    }

    private static int Reg(params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("reg.exe")
        {
            Arguments = string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        process!.WaitForExit(15000);
        return process.ExitCode;
    }
}
