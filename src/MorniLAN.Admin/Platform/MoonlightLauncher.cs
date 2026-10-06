using System.Diagnostics;
using Microsoft.Win32;

namespace MorniLAN.Admin.Platform;

/// <summary>
/// Startet Moonlight (den Streaming-Client) auf dem Admin-PC automatisch, sobald der Fernzugriff läuft – damit ein
/// Klick auf „Verbinden" reicht. Findet Moonlight an den üblichen Orten; fehlt es, sagt das Panel, wie man es holt.
/// Die einmalige Kopplung (PIN) zwischen Moonlight und Sunshine bleibt ein manueller Schritt pro PC-Paar.
/// </summary>
public static class MoonlightLauncher
{
    public const string WingetId = "MoonlightGameStreamingProject.Moonlight";

    /// <summary>Ist Moonlight installiert?</summary>
    public static bool IsInstalled() => FindExecutable() is not null;

    /// <summary>Öffnet Moonlight für diesen Host (IP ohne Port). true, wenn gestartet; false, wenn Moonlight fehlt.</summary>
    public static bool TryStream(string hostWithPort)
    {
        if (FindExecutable() is not { } exe)
            return false;
        var host = hostWithPort.Split(':')[0]; // Port weglassen – Moonlight nutzt seinen Standard
        try
        {
            // "stream <host> Desktop" verbindet direkt; ist noch nicht gekoppelt, zeigt Moonlight die PIN-Kopplung an
            Process.Start(new ProcessStartInfo(exe, $"stream {host} \"Desktop\"") { UseShellExecute = false });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? FindExecutable()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Moonlight Game Streaming", "Moonlight.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Moonlight Game Streaming", "Moonlight.exe"),
                 })
            if (File.Exists(path))
                return path;

        // winget legt eine Verknüpfung unter %LOCALAPPDATA%\Microsoft\WinGet\Links an
        var wingetLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Links", "Moonlight.exe");
        if (File.Exists(wingetLink))
            return wingetLink;

        foreach (var keyPath in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Moonlight.exe",
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\Moonlight.exe" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key?.GetValue(null) is string p && File.Exists(p))
                return p;
        }
        return null;
    }
}
