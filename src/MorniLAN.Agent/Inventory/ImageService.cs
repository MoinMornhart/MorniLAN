using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Inventory;

/// <summary>
/// Erzeugt Icons (64×64 PNG) und Cover (max. 300×450 JPEG) und merkt sie sich über ihren SHA-256-Hash.
/// Gleiche Dateien werden nur einmal verarbeitet (Schlüssel: Pfad + Änderungszeit + Größe).
/// </summary>
internal sealed class ImageService
{
    public const int IconSize = 64;
    public const int CoverWidth = 300;
    public const int CoverHeight = 450;

    private readonly ConcurrentDictionary<string, AppImage> _byHash = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _bySource = new(StringComparer.OrdinalIgnoreCase);

    public AppImage? Get(string hash) => _byHash.GetValueOrDefault(hash);

    /// <summary>Icon und Cover eintragen. Fehler bei einzelnen Bildern lassen den Eintrag ohne Bild.</summary>
    public AppEntry WithImages(InventoryItem item)
    {
        var sources = item.Images;
        var icon = Cached("icon-file", sources.IconFile, path => Encode(LoadScaled(path, IconSize, IconSize), ImageFormat.Png))
                   ?? Cached("icon-exe", sources.IconFromExecutable, ExtractExeIcon);
        var cover = Cached("cover", sources.CoverFile, path => Encode(LoadScaled(path, CoverWidth, CoverHeight), ImageFormat.Jpeg));
        return item.App with { IconHash = icon, CoverHash = cover };
    }

    private string? Cached(string kind, string? path, Func<string, AppImage?> create)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        var info = new FileInfo(path);
        var key = $"{kind}|{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        return _bySource.GetOrAdd(key, _ =>
        {
            try
            {
                var image = create(path);
                if (image is null)
                    return null;
                _byHash.TryAdd(image.Hash, image);
                return image.Hash;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
            {
                return null; // kaputtes oder gesperrtes Bild: dann eben ohne
            }
        });
    }

    private static AppImage? ExtractExeIcon(string exePath)
    {
        using var icon = Icon.ExtractIcon(exePath, 0, IconSize);
        if (icon is null)
            return null;
        using var bitmap = icon.ToBitmap();
        return Encode(Fit(bitmap, IconSize, IconSize), ImageFormat.Png);
    }

    private static Bitmap LoadScaled(string path, int maxWidth, int maxHeight)
    {
        using var stream = File.OpenRead(path);
        using var source = Image.FromStream(stream);
        return Fit(source, maxWidth, maxHeight);
    }

    /// <summary>Verkleinert proportional (nie vergrößern über das Doppelte), transparenter Hintergrund.</summary>
    internal static Bitmap Fit(Image source, int maxWidth, int maxHeight)
    {
        var scale = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
        scale = Math.Min(scale, 2.0);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(target);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(source, 0, 0, width, height);
        return target;
    }

    internal static AppImage Encode(Bitmap bitmap, ImageFormat format)
    {
        using (bitmap)
        using (var output = new MemoryStream())
        {
            if (format.Equals(ImageFormat.Jpeg))
            {
                var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
                bitmap.Save(output, encoder, parameters);
            }
            else
            {
                bitmap.Save(output, ImageFormat.Png);
            }
            var data = output.ToArray();
            return new AppImage(Convert.ToHexStringLower(SHA256.HashData(data)),
                format.Equals(ImageFormat.Jpeg) ? "image/jpeg" : "image/png", data);
        }
    }
}
