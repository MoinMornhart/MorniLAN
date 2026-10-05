using System.Text.Json;
using System.Text.Json.Serialization;
using MorniLAN.Admin.Server;

namespace MorniLAN.Admin.Platform;

/// <summary>Einstellungen des Panels in %LOCALAPPDATA%\MorniLAN\admin\settings.json.</summary>
public sealed record AdminSettings(bool KeepRunningInTray = true)
{
    private static string FilePath => Path.Combine(AdminPaths.Data, "settings.json");

    public static AdminSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize(File.ReadAllBytes(FilePath), AdminSettingsJson.Default.AdminSettings) ?? new()
                : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AdminPaths.Data);
        File.WriteAllBytes(FilePath, JsonSerializer.SerializeToUtf8Bytes(this, AdminSettingsJson.Default.AdminSettings));
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AdminSettings))]
internal sealed partial class AdminSettingsJson : JsonSerializerContext;
