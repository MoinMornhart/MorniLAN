using System.Text.Json;
using System.Text.Json.Serialization;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;

namespace MorniLAN.Launcher;

/// <summary>Was sich der Launcher für dieses Windows-Konto merkt (nichts davon ist eine Freigabe).</summary>
internal sealed record LauncherMemory
{
    public string? LastProfileId { get; init; }

    /// <summary>Zuletzt gestartete Einträge je Profil (neueste zuerst). Schlüssel <see cref="LauncherAppList.NoProfile"/> ohne Profil.</summary>
    public Dictionary<string, string[]> Recent { get; init; } = [];
}

[JsonSerializable(typeof(LauncherMemory))]
internal sealed partial class LauncherJsonContext : JsonSerializerContext;

/// <summary>
/// Liegt in %LOCALAPPDATA%\MorniLAN\launcher\memory.json. Räumt außerdem den eigenen Briefkasten auf: Der Dienst
/// löscht dort absichtlich nichts (siehe LauncherInboxService), also tut es der Launcher.
/// </summary>
internal sealed class LauncherStore
{
    public const int MaxRecent = 10;

    private readonly string _file;

    public LauncherStore(string? folder = null)
    {
        var root = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MorniLAN", "launcher");
        Directory.CreateDirectory(root);
        _file = Path.Combine(root, "memory.json");
        Memory = Load();
    }

    public LauncherMemory Memory { get; private set; }

    public IReadOnlyList<string> RecentFor(string? profileId) =>
        Memory.Recent.GetValueOrDefault(profileId ?? LauncherAppList.NoProfile) ?? [];

    public void RememberLaunch(string? profileId, string appId)
    {
        var key = profileId ?? LauncherAppList.NoProfile;
        var list = new[] { appId }.Concat(RecentFor(profileId).Where(id => id != appId)).Take(MaxRecent).ToArray();
        Memory = Memory with { Recent = new Dictionary<string, string[]>(Memory.Recent) { [key] = list } };
        Save();
    }

    public void RememberProfile(string? profileId)
    {
        Memory = Memory with { LastProfileId = profileId };
        Save();
    }

    /// <summary>Eigene Briefkasten-Dateien, die der Dienst längst gelesen hat (oder nie lesen wird), entfernen.</summary>
    public static void CleanInbox(string folder, TimeSpan olderThan)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > olderThan)
                    File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private LauncherMemory Load()
    {
        try
        {
            return File.Exists(_file)
                ? JsonSerializer.Deserialize(File.ReadAllBytes(_file), LauncherJsonContext.Default.LauncherMemory) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllBytes(_file + ".tmp", JsonSerializer.SerializeToUtf8Bytes(Memory, LauncherJsonContext.Default.LauncherMemory));
            File.Move(_file + ".tmp", _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
