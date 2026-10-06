using System.Security.Cryptography;
using System.Text;

namespace MorniLAN.Shared.Models;

/// <summary>
/// Erzeugt stabile IDs für App-Einträge, damit Freigaben über Neuinstallationen
/// und Neustarts hinweg erhalten bleiben.
/// </summary>
public static class AppId
{
    public static string ForSteam(int steamAppId) =>
        steamAppId > 0
            ? $"steam:{steamAppId}"
            : throw new ArgumentOutOfRangeException(nameof(steamAppId), "Steam-AppID muss positiv sein.");

    /// <summary>ID aus dem EXE-Pfad (Groß-/Kleinschreibung und Slash-Richtung egal).</summary>
    public static string ForExecutable(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var normalized = executablePath.Trim().Replace('/', '\\').ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"exe:{Convert.ToHexStringLower(hash.AsSpan(0, 8))}";
    }

    /// <summary>
    /// Spiel über die ID des Launchers (z. B. "epic:fortnite", "gog:1207658924"): bleibt gleich, auch wenn das
    /// Spiel auf ein anderes Laufwerk umzieht.
    /// </summary>
    public static string ForLauncher(string prefix, string launcherId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherId);
        return $"{prefix}:{launcherId.Trim().ToLowerInvariant()}";
    }

    public static string ForCustom(Guid id) => $"custom:{id:N}";

    /// <summary>Store-App über ihre AppUserModelId (Familienname + App-ID, versionsunabhängig).</summary>
    public static string ForStoreApp(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        return $"store:{appUserModelId.Trim().ToLowerInvariant()}";
    }

    /// <summary>Programm ohne bekannte EXE, über seinen Uninstall-Schlüssel (z. B. "{GUID}" oder "Discord").</summary>
    public static string ForUninstallKey(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(keyName.Trim().ToUpperInvariant()));
        return $"prog:{Convert.ToHexStringLower(hash.AsSpan(0, 8))}";
    }
}
