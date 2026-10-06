using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MorniLAN.Launcher;

/// <summary>
/// Startet eine Kachel. Store-Apps über die Aktivierungs-Schnittstelle von Windows (nicht über explorer.exe:
/// Als Shell-Ersatz läuft kein Explorer, und der Aufruf könnte ihn als Desktop nachstarten). Links wie steam://
/// oder com.epicgames.launcher:// und EXE-Dateien über ShellExecute.
/// </summary>
internal static class AppStarter
{
    private const string StorePrefix = @"shell:AppsFolder\";

    /// <summary>Fehlermeldung für die Anzeige, oder null bei Erfolg.</summary>
    public static string? Start(string target, string? arguments)
    {
        try
        {
            if (target.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var manager = (IApplicationActivationManager)new ApplicationActivationManager();
                manager.ActivateApplication(target[StorePrefix.Length..], arguments, 0, out _);
                return null;
            }

            var info = new ProcessStartInfo(target) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(arguments))
                info.Arguments = arguments;
            if (File.Exists(target))
                info.WorkingDirectory = Path.GetDirectoryName(target)!;
            else if (!target.Contains("://", StringComparison.Ordinal))
                return "Das Programm wurde auf diesem PC nicht gefunden.";
            using var process = Process.Start(info);
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or COMException or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager;

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    // Ohne PreserveSig wird ein Fehler-HRESULT zur COMException
    private interface IApplicationActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);

        void ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);

        void ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray,
            out uint processId);
    }
}
