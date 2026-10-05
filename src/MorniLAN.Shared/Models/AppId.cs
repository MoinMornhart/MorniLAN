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

    public static string ForCustom(Guid id) => $"custom:{id:N}";
}
