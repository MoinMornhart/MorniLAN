using Microsoft.Win32;

namespace MorniLAN.Agent.Platform;

/// <summary>
/// Läuft gerade ein Steam-Spiel? Steam schreibt die AppID des laufenden Spiels nach
/// HKCU\Software\Valve\Steam\RunningAppID (0 = keins). Als Dienst lesen wir die Hives aller angemeldeten Benutzer.
/// </summary>
internal static class SteamActivity
{
    public static bool IsGameRunning() => RunningAppId() is > 0;

    public static int? RunningAppId()
    {
        try
        {
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default);
            foreach (var sid in users.GetSubKeyNames().Where(Inventory.UninstallRegistryScanner.IsUserSid))
            {
                using var steam = users.OpenSubKey($@"{sid}\Software\Valve\Steam");
                if (steam?.GetValue("RunningAppID") is int appId && appId > 0)
                    return appId;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return null;
    }
}
