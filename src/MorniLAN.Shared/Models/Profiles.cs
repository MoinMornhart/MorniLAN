using System.Text.Json;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Shared.Models;

/// <summary>Eine Person am verwalteten PC (mehrere teilen sich ein Windows-Konto, wie bei einer Konsole).</summary>
/// <param name="Color">Farbe der Profilkachel, einer der Werte aus <see cref="ProfileColors"/>.</param>
public sealed record LauncherProfile(string Id, string Name, string Color, DateTimeOffset CreatedAt)
{
    public const int MaxNameLength = 24;
    public const int MaxProfiles = 12;

    /// <summary>Fehlermeldung für die Eingabe, oder null, wenn der Name passt.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Bitte einen Namen eingeben.";
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            return $"Höchstens {MaxNameLength} Zeichen.";
        if (trimmed.Any(char.IsControl))
            return "Der Name enthält ungültige Zeichen.";
        return null;
    }

    public static string NewId() => $"profile:{Guid.NewGuid():N}";
}

/// <summary>Feste Farbauswahl für Profile (Hex), damit fremde Eingaben keine beliebigen Werte setzen.</summary>
public static class ProfileColors
{
    public static readonly string[] All = ["#4C8DFF", "#3DDC84", "#E8B04B", "#FF6B6B", "#B07CFF", "#2EC4D6", "#FF8FB1", "#9AA3B2"];

    public static string Normalize(string? color) =>
        All.FirstOrDefault(c => string.Equals(c, color, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

/// <summary>Ausnahmen, die nur für ein Profil gelten (gehen den Regeln des PCs vor).</summary>
public sealed record ProfileRuleSet(string ProfileId, ApprovalRule[] Rules);

/// <summary>„Hilfe anfordern“ aus dem Launcher.</summary>
public sealed record HelpRequest(string Id, string? ProfileName, DateTimeOffset CreatedAt);

/// <summary>
/// Rückweg Launcher → Agent. Der Launcher (Benutzerrechte) legt kleine JSON-Dateien in seinen eigenen Ordner
/// %LOCALAPPDATA%\MorniLAN\launcher\inbox, der Agent (SYSTEM) holt sie ab, prüft sie und löscht sie.
/// So bleibt die Pipe nur lesbar (keine Angriffsfläche fürs Pipe-Squatting). Alles darin ist Benutzereingabe.
/// </summary>
public static class LauncherInbox
{
    public const int MaxFileBytes = 4096;
    public const string ProfilePrefix = "profile-";
    public const string HelpPrefix = "help-";

    /// <summary>Ordner unterhalb des Benutzerprofils (z. B. C:\Users\Freund).</summary>
    public static string FolderFor(string userProfileDirectory) =>
        Path.Combine(userProfileDirectory, "AppData", "Local", "MorniLAN", "launcher", "inbox");

    /// <summary>Ordner des angemeldeten Benutzers (für den Launcher).</summary>
    public static string CurrentUserFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MorniLAN", "launcher", "inbox");

    public sealed record ProfileRequest(string Name, string Color);

    public sealed record HelpMessage(string? ProfileName);

    public static void WriteProfileRequest(string folder, ProfileRequest request) =>
        Write(folder, ProfilePrefix, JsonSerializer.SerializeToUtf8Bytes(request, MorniLanJsonContext.Default.ProfileRequest));

    public static void WriteHelpRequest(string folder, HelpMessage message) =>
        Write(folder, HelpPrefix, JsonSerializer.SerializeToUtf8Bytes(message, MorniLanJsonContext.Default.HelpMessage));

    private static void Write(string folder, string prefix, byte[] data)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{prefix}{Guid.NewGuid():N}.json");
        File.WriteAllBytes(file + ".tmp", data);
        File.Move(file + ".tmp", file);
    }
}
