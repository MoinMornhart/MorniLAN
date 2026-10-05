using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Admin.Server;

/// <summary>
/// Programmlisten je Gerät (inventory\&lt;DeviceId&gt;.json) und alle Bilder (images\&lt;hash&gt;.png|jpg).
/// Bilder werden nur angenommen, wenn Hash, Größe und Dateityp stimmen.
/// </summary>
public sealed partial class InventoryStore
{
    private readonly string _inventoryDir;
    private readonly string _imageDir;
    private readonly ConcurrentDictionary<Guid, InventoryReport> _reports = new();

    public event Action<Guid>? Changed;

    public InventoryStore(string dataDirectory)
    {
        _inventoryDir = Path.Combine(dataDirectory, "inventory");
        _imageDir = Path.Combine(dataDirectory, "images");
        Directory.CreateDirectory(_inventoryDir);
        Directory.CreateDirectory(_imageDir);
    }

    public InventoryReport? Get(Guid deviceId)
    {
        if (_reports.TryGetValue(deviceId, out var report))
            return report;
        var file = InventoryFile(deviceId);
        if (!File.Exists(file))
            return null;
        try
        {
            report = JsonSerializer.Deserialize(File.ReadAllBytes(file), MorniLanJsonContext.Default.InventoryReport);
        }
        catch (JsonException)
        {
            return null;
        }
        return report is null ? null : _reports.GetOrAdd(deviceId, report);
    }

    public void Save(Guid deviceId, InventoryReport report)
    {
        _reports[deviceId] = report;
        var file = InventoryFile(deviceId);
        var temp = file + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(report, MorniLanJsonContext.Default.InventoryReport));
        File.Move(temp, file, overwrite: true);
        Changed?.Invoke(deviceId);
    }

    public void Remove(Guid deviceId)
    {
        _reports.TryRemove(deviceId, out _);
        File.Delete(InventoryFile(deviceId));
        Changed?.Invoke(deviceId);
    }

    /// <summary>Bild-Hashes aus der Liste, die hier noch fehlen.</summary>
    public string[] MissingImages(InventoryReport report) =>
        [.. report.Apps.SelectMany(a => new[] { a.IconHash, a.CoverHash })
            .OfType<string>()
            .Where(IsHash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(h => ImagePath(h) is null)];

    /// <summary>Speichert ein Bild, wenn es zum Hash passt. Gibt false bei allem Verdächtigen zurück.</summary>
    public bool TrySaveImage(AppImage image)
    {
        if (!IsHash(image.Hash) || image.Data.Length is 0 or > ConnectionDefaults.MaxImageBytes)
            return false;
        var extension = image.ContentType switch
        {
            "image/png" when IsPng(image.Data) => ".png",
            "image/jpeg" when IsJpeg(image.Data) => ".jpg",
            _ => null,
        };
        if (extension is null
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(image.Data)), image.Hash, StringComparison.OrdinalIgnoreCase))
            return false;

        var path = Path.Combine(_imageDir, image.Hash.ToLowerInvariant() + extension);
        if (!File.Exists(path))
            File.WriteAllBytes(path, image.Data);
        return true;
    }

    /// <summary>Pfad zum gespeicherten Bild, oder null.</summary>
    public string? ImagePath(string? hash)
    {
        if (hash is null || !IsHash(hash))
            return null;
        foreach (var extension in new[] { ".png", ".jpg" })
        {
            var path = Path.Combine(_imageDir, hash.ToLowerInvariant() + extension);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private string InventoryFile(Guid deviceId) => Path.Combine(_inventoryDir, $"{deviceId:N}.json");

    private static bool IsPng(byte[] data) => data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
    private static bool IsJpeg(byte[] data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    internal static bool IsHash(string value) => HashPattern().IsMatch(value);

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex HashPattern();
}
