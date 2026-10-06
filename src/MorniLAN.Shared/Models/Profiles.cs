using System.Text.Json;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Shared.Models;

/// <summary>Eine Person am verwalteten PC (mehrere teilen sich ein Windows-Konto, wie bei einer Konsole).</summary>
/// <param name="Color">Farbe der Profilkachel, einer der Werte aus <see cref="ProfileColors"/>.</param>
/// <param name="Theme">Hintergrund-Thema aus <see cref="ProfileThemes"/> ("custom" = eigenes Bild, das der Launcher lokal hält).</param>
/// <param name="PasswordHash">PBKDF2-Hash, falls das Profil durch ein Passwort geschützt ist (nie im Klartext).</param>
/// <param name="HasCustomBackground">Der Nutzer hat ein eigenes Hintergrundbild gewählt (liegt lokal beim Launcher).</param>
/// <param name="HasAvatar">Der Nutzer hat ein eigenes Profilbild gewählt (liegt lokal beim Launcher).</param>
public sealed record LauncherProfile(
    string Id,
    string Name,
    string Color,
    DateTimeOffset CreatedAt,
    string? Theme = null,
    string? PasswordHash = null,
    bool HasCustomBackground = false,
    bool HasAvatar = false)
{
    public const int MaxNameLength = 24;
    public const int MaxProfiles = 12;

    /// <summary>Ist das Profil durch ein Passwort geschützt?</summary>
    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);

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

/// <summary>
/// Fertige Hintergrund-Themen für den Launcher (Gaming-Look). "default" = schlicht dunkel, "custom" = eigenes Bild.
/// Zwei Farben je Thema für einen Farbverlauf; die Oberfläche zeichnet sie, es braucht keine Bilddateien.
/// </summary>
public static class ProfileThemes
{
    public const string Default = "default";
    public const string Custom = "custom";

    /// <summary>Thema-ID → (Anzeigename, obere Farbe, untere Farbe).</summary>
    public static readonly IReadOnlyList<(string Id, string Name, string From, string To)> Gradients =
    [
        (Default, "Schlicht", "#0F1115", "#0F1115"),
        ("neon", "Neon", "#1B1035", "#0A1E2E"),
        ("sunset", "Sonnenuntergang", "#2A1020", "#2E1A0A"),
        ("forest", "Wald", "#0A2018", "#0A1420"),
        ("racing", "Racing", "#2A0A0A", "#101014"),
        ("ocean", "Ozean", "#0A1A2E", "#061015"),
    ];

    public static bool IsKnown(string? theme) => theme == Custom || Gradients.Any(g => g.Id == theme);

    public static string Normalize(string? theme) => IsKnown(theme) ? theme! : Default;
}

/// <summary>Passwort-Schutz für Profile: PBKDF2-Hash, nie Klartext. Schützt nur die Profilauswahl im Launcher.</summary>
public static class ProfilePassword
{
    private const int Iterations = 120_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    public const int MinLength = 4;
    public const int MaxLength = 64;

    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Bitte ein Passwort eingeben.";
        if (password.Length < MinLength)
            return $"Mindestens {MinLength} Zeichen.";
        if (password.Length > MaxLength)
            return $"Höchstens {MaxLength} Zeichen.";
        return null;
    }

    /// <summary>Erzeugt "pbkdf2$&lt;salt&gt;$&lt;hash&gt;" (Base64). Für die Ablage im Profil.</summary>
    public static string Hash(string password)
    {
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256, HashBytes);
        return $"pbkdf2${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Prüft ein eingegebenes Passwort gegen den gespeicherten Hash (zeitkonstanter Vergleich).</summary>
    public static bool Verify(string? password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
            return false;
        var parts = stored.Split('$');
        if (parts.Length != 3 || parts[0] != "pbkdf2")
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations,
                System.Security.Cryptography.HashAlgorithmName.SHA256, expected.Length);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Sieht der Wert nach einem gültigen Hash aus? (Zum Prüfen von Daten, die über den Briefkasten kommen.)</summary>
    public static bool LooksLikeHash(string? value)
    {
        if (value is null)
            return true; // kein Passwort ist ok
        var parts = value.Split('$');
        return parts.Length == 3 && parts[0] == "pbkdf2";
    }
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
    public const string EditPrefix = "edit-";
    public const string HelpPrefix = "help-";
    public const string RemotePrefix = "remote-";
    public const string ConnPrefix = "conn-";

    /// <summary>Ordner unterhalb des Benutzerprofils (z. B. C:\Users\Freund).</summary>
    public static string FolderFor(string userProfileDirectory) =>
        Path.Combine(userProfileDirectory, "AppData", "Local", "MorniLAN", "launcher", "inbox");

    /// <summary>Ordner des angemeldeten Benutzers (für den Launcher).</summary>
    public static string CurrentUserFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MorniLAN", "launcher", "inbox");

    public sealed record ProfileRequest(string Name, string Color);

    public sealed record HelpMessage(string? ProfileName);

    /// <summary>
    /// Der Nutzer passt sein Profil im Launcher an. Nur gesetzte Felder ändern etwas. PasswordHash: null = unverändert,
    /// "" = Passwort entfernen, sonst neuer Hash. Das Klartext-Passwort verlässt den Launcher nie.
    /// </summary>
    public sealed record ProfileEdit(
        string ProfileId,
        string? Color = null,
        string? Theme = null,
        string? PasswordHash = null,
        bool? HasCustomBackground = null,
        bool? HasAvatar = null);

    public static void WriteProfileRequest(string folder, ProfileRequest request) =>
        Write(folder, ProfilePrefix, JsonSerializer.SerializeToUtf8Bytes(request, MorniLanJsonContext.Default.ProfileRequest));

    public static void WriteProfileEdit(string folder, ProfileEdit edit) =>
        Write(folder, EditPrefix, JsonSerializer.SerializeToUtf8Bytes(edit, MorniLanJsonContext.Default.ProfileEdit));

    public static void WriteHelpRequest(string folder, HelpMessage message) =>
        Write(folder, HelpPrefix, JsonSerializer.SerializeToUtf8Bytes(message, MorniLanJsonContext.Default.HelpMessage));

    /// <summary>Antwort des Freundes auf die Fernzugriffs-Anfrage (erlauben/ablehnen).</summary>
    public sealed record RemoteConsent(bool Allow);

    public static void WriteRemoteConsent(string folder, RemoteConsent consent) =>
        Write(folder, RemotePrefix, JsonSerializer.SerializeToUtf8Bytes(consent, MorniLanJsonContext.Default.RemoteConsent));

    /// <summary>
    /// Adresse des Admin-PCs, am Gerät im Launcher eingegeben (für den Fall, dass die automatische Suche scheitert,
    /// z. B. Tailscale oder ein anderes Netz). Der Agent übernimmt sie und verbindet sich damit.
    /// </summary>
    public sealed record ConnectionRequest(string AdminHost)
    {
        public const int MaxHostLength = 120;

        /// <summary>Fehlertext oder null. Erlaubt Hostnamen, IPv4/IPv6 und optional ":Port" – nichts Exotisches.</summary>
        public static string? Validate(string? host)
        {
            var value = host?.Trim();
            if (string.IsNullOrEmpty(value))
                return "Bitte eine Adresse eingeben, z. B. 192.168.1.50 oder admin-pc.";
            if (value.Length > MaxHostLength)
                return "Die Adresse ist zu lang.";
            if (value.Any(c => char.IsControl(c) || c is ' ' or '/' or '\\' or '"' or '\''))
                return "Die Adresse enthält ungültige Zeichen.";
            return null;
        }
    }

    public static void WriteConnectionRequest(string folder, ConnectionRequest request) =>
        Write(folder, ConnPrefix, JsonSerializer.SerializeToUtf8Bytes(request, MorniLanJsonContext.Default.ConnectionRequest));

    private static void Write(string folder, string prefix, byte[] data)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{prefix}{Guid.NewGuid():N}.json");
        File.WriteAllBytes(file + ".tmp", data);
        File.Move(file + ".tmp", file);
    }
}
