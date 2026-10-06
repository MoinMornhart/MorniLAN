using System.Text.Json;
using System.Text.Json.Serialization;
using MorniLAN.Admin.Server;

namespace MorniLAN.Admin.Platform;

/// <summary>Einstellungen des Panels in %LOCALAPPDATA%\MorniLAN\admin\settings.json.</summary>
/// <param name="SetupCompleted">
/// Der Einrichtungs-Assistent wurde schon durchlaufen oder übersprungen. Standard false: Beim allerersten Start
/// (solange noch kein PC gekoppelt ist) führt der Assistent durch Firewall und erstes Pairing.
/// </param>
public sealed record AdminSettings(bool KeepRunningInTray = true, bool SetupCompleted = false)
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
