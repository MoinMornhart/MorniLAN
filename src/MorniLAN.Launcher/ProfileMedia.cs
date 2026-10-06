using Avalonia.Media.Imaging;

namespace MorniLAN.Launcher;

/// <summary>
/// Hintergrundbilder und Avatare der Profile, die der Nutzer selbst gewählt hat. Liegen lokal beim Launcher
/// (%LOCALAPPDATA%\MorniLAN\launcher\profiles\&lt;id&gt;), weil sie groß sind und nur am PC gebraucht werden.
/// </summary>
public sealed class ProfileMedia
{
    private readonly string _root;

    public ProfileMedia(string? root = null)
    {
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MorniLAN", "launcher", "profiles");
        Directory.CreateDirectory(_root);
    }

    public string BackgroundPath(string profileId) => Path.Combine(Folder(profileId), "background.png");
    public string AvatarPath(string profileId) => Path.Combine(Folder(profileId), "avatar.png");

    public bool HasBackground(string profileId) => File.Exists(BackgroundPath(profileId));
    public bool HasAvatar(string profileId) => File.Exists(AvatarPath(profileId));

    /// <summary>Kopiert die gewählte Bilddatei verkleinert in den Profilordner. true bei Erfolg.</summary>
    public bool SaveBackground(string profileId, string sourceFile) =>
        SaveScaled(sourceFile, BackgroundPath(profileId), 1920, 1080);

    public bool SaveAvatar(string profileId, string sourceFile) =>
        SaveScaled(sourceFile, AvatarPath(profileId), 256, 256);

    public void Remove(string profileId)
    {
        try
        {
            if (Directory.Exists(Folder(profileId)))
                Directory.Delete(Folder(profileId), recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public Bitmap? Load(string path)
    {
        try { return File.Exists(path) ? new Bitmap(path) : null; }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { return null; }
    }

    private string Folder(string profileId)
    {
        // Profil-ID ("profile:<guid>") in einen sicheren Ordnernamen umwandeln
        var safe = new string(profileId.Where(c => char.IsLetterOrDigit(c)).ToArray());
        var folder = Path.Combine(_root, safe.Length > 0 ? safe : "x");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private bool SaveScaled(string sourceFile, string targetPath, int maxWidth, int maxHeight)
    {
        try
        {
            using var source = new Bitmap(sourceFile);
            var scale = Math.Min((double)maxWidth / source.PixelSize.Width, (double)maxHeight / source.PixelSize.Height);
            scale = Math.Min(scale, 1.0);
            var width = Math.Max(1, (int)(source.PixelSize.Width * scale));
            var height = Math.Max(1, (int)(source.PixelSize.Height * scale));
            using var resized = source.CreateScaledBitmap(new Avalonia.PixelSize(width, height));
            var temp = targetPath + ".tmp";
            using (var stream = File.Create(temp))
#pragma warning disable CS0618
                resized.Save(stream); // Avalonia speichert als PNG; für Hintergründe reicht das
#pragma warning restore CS0618
            File.Move(temp, targetPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }
}
