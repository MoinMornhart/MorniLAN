using Microsoft.Win32;

namespace MorniLAN.Agent.Restrictions;

/// <summary>
/// Findet den MorniLAN-Launcher und baut das Kommando, mit dem er als Desktop (Shell) eines Kontos startet.
/// Quelle ist der Autostart-Eintrag des Geräte-Setups (HKLM\…\Run „MorniLAN Launcher“) – so muss der Pfad
/// nirgends doppelt gepflegt werden. Gesetzt wird die Shell über <see cref="UserPolicyWriter.SetShell"/>.
/// </summary>
internal static class KioskShell
{
    /// <summary>Der Launcher soll wissen, dass er die Shell ist (Vollbild, kann nicht geschlossen werden).</summary>
    public const string ShellArgument = "--shell";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "MorniLAN Launcher";

    /// <summary>
    /// Kommando für Winlogon\Shell, z. B. <c>"C:\Program Files\MorniLAN\Launcher\MorniLAN.Launcher.exe" --shell</c>,
    /// oder null, wenn der Launcher nicht gefunden wird. Dann wird KEIN Shell-Ersatz gesetzt – ein Konto ohne
    /// funktionierenden Desktop wäre schlimmer als gar kein Kiosk.
    /// </summary>
    public static string? ResolveLauncherCommand() => ResolveLauncherPath() is { } path ? $"\"{path}\" {ShellArgument}" : null;

    /// <summary>Pfad zur MorniLAN.Launcher.exe, aus dem Autostart-Eintrag gelesen und auf Existenz geprüft.</summary>
    public static string? ResolveLauncherPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunKey);
            if (key?.GetValue(RunValue) is string command)
            {
                var path = ExtractExecutable(command);
                if (path is not null && File.Exists(path))
                    return path;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>Holt den (ggf. in Anführungszeichen stehenden) EXE-Pfad aus einer Kommandozeile.</summary>
    internal static string? ExtractExecutable(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
            return null;
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }
}
