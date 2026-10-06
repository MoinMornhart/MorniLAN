namespace MorniLAN.Shared.Updates;

/// <summary>
/// Merkt die letzte GitHub-Antwort samt ETag, damit die nächste Abfrage „If-None-Match" schicken und ein 304
/// (unverändert) den letzten Stand ohne neue Datenübertragung wiederverwenden kann. Bewusst ein ganz einfaches
/// Dateiformat (erste Zeile ETag, danach der JSON-Rumpf), damit nichts per Reflection serialisiert werden muss
/// (trimming-/AOT-sicher). Fehler beim Lesen/Schreiben sind nie schlimm: dann wird eben normal neu geladen.
/// </summary>
internal static class ReleaseCache
{
    internal sealed record Entry(string ETag, string Body);

    private static string FilePath(string directory) => Path.Combine(directory, "releases-cache.json");

    public static Entry? Read(string directory)
    {
        try
        {
            var text = File.ReadAllText(FilePath(directory));
            var newline = text.IndexOf('\n');
            if (newline <= 0)
                return null;
            var etag = text[..newline].TrimEnd('\r');
            return etag.Length == 0 ? null : new Entry(etag, text[(newline + 1)..]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(string directory, string etag, string body)
    {
        try
        {
            Directory.CreateDirectory(directory);
            // Atomar: erst in .tmp, dann verschieben – ein Absturz hinterlässt sonst eine halbe Datei, deren ETag
            // weiter passt (304) und die beim Parsen ewig wirft.
            var path = FilePath(directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, etag + "\n" + body);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache ist nur Beschleunigung – ohne ihn wird einfach jedes Mal frisch geladen
        }
    }

    public static void Delete(string directory)
    {
        try { File.Delete(FilePath(directory)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
