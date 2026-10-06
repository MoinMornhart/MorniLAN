using System.Globalization;

namespace MorniLAN.Agent.Updates;

/// <summary>
/// Merkt vor einer Installation, auf welche Version aktualisiert werden soll. Das Setup beendet den Dienst und
/// startet ihn neu – beim nächsten Start prüft der Agent diesen Marker: Läuft jetzt die Zielversion, hat es
/// geklappt; läuft immer noch die alte, ist das Update nicht durchgekommen und wird gemeldet (und erneut versucht).
/// Bewusst eine einfache Datei (erste Zeile Version, zweite Zeitstempel), robust gegen Lese-/Schreibfehler.
/// </summary>
internal static class UpdateMarker
{
    public sealed record Pending(string TargetVersion, DateTimeOffset StartedAt);

    private static string FilePath(string directory) => Path.Combine(directory, "update-pending.txt");

    public static void Write(string directory, string targetVersion, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath(directory), targetVersion + "\n" + now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ohne Marker fehlt nur die Erfolgskontrolle – das Update selbst läuft trotzdem
        }
    }

    public static Pending? Read(string directory)
    {
        try
        {
            var lines = File.ReadAllLines(FilePath(directory));
            if (lines.Length < 1 || lines[0].Length == 0)
                return null;
            var at = lines.Length > 1 && long.TryParse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs)
                ? DateTimeOffset.FromUnixTimeSeconds(secs)
                : DateTimeOffset.UnixEpoch;
            return new Pending(lines[0].Trim(), at);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Clear(string directory)
    {
        try { File.Delete(FilePath(directory)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
