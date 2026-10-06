using System.Diagnostics;
using System.Security.Principal;

namespace MorniLAN.Agent.Restrictions;

/// <summary>
/// Notfall-Entsperrung am PC (Nutzer-Entscheidung M6): hebt alle Sperren sofort auf, auch wenn das Panel nicht
/// erreichbar ist. Start über den Startmenü-Eintrag „MorniLAN Notfall-Entsperrung“; Windows fragt nach
/// Administrator-Rechten (also dem Admin-Passwort). Danach setzt erst das Panel die Sperren wieder in Gang.
/// </summary>
internal static class EmergencyUnlock
{
    public const string Argument = "--emergency-unlock";

    /// <summary>Datei im Datenordner: Solange sie da ist, setzt der Dienst keine Sperren (siehe RestrictionService).</summary>
    public static string PausedFile(string dataDirectory) => Path.Combine(dataDirectory, "restrictions-paused");

    /// <summary>
    /// Führt die Entsperrung aus. Ohne Admin-Rechte wird mit UAC neu gestartet (Admin-Passwort). Gibt den Exitcode
    /// fürs Hauptprogramm zurück.
    /// </summary>
    public static int Run(string dataDirectory)
    {
        if (!IsElevated())
            return Elevate();

        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(PausedFile(dataDirectory),
                $"Entsperrt am {DateTimeOffset.Now:yyyy-MM-dd HH:mm} – der Admin aktiviert die Sperren im Panel wieder.");
            foreach (var account in AccountScanner.Scan())
            {
                // Richtlinien entfernen UND den normalen Windows-Desktop wiederherstellen (falls Kiosk aktiv war).
                try { UserPolicyWriter.Clear(account.Sid); }
                catch (Exception ex) { Console.Error.WriteLine($"{account.Name}: {ex.Message}"); }
                try { UserPolicyWriter.ClearShell(account.Sid); }
                catch (Exception ex) { Console.Error.WriteLine($"{account.Name} (Desktop): {ex.Message}"); }
            }
            AppControl.Disable();
            Console.WriteLine("Alle MorniLAN-Sperren wurden aufgehoben. Der PC verhält sich wieder normal.");
            Console.WriteLine("Der normale Windows-Desktop (explorer.exe) ist beim nächsten Anmelden wieder da.");
            Console.WriteLine("Hinweis: Der Admin kann die Sperren im Panel wieder einschalten.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Notfall-Entsperrung fehlgeschlagen: {ex.Message}");
            return 1;
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Startet sich selbst mit Administrator-Rechten neu (UAC-Abfrage).</summary>
    private static int Elevate()
    {
        try
        {
            using var elevated = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                Arguments = Argument,
                UseShellExecute = true,
                Verb = "runas",
            });
            elevated?.WaitForExit();
            return elevated?.ExitCode ?? 1;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine("Für die Notfall-Entsperrung werden Administrator-Rechte benötigt.");
            return 1;
        }
    }
}
