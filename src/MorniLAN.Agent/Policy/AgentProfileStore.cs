using System.Text.Json;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Agent.Policy;

/// <summary>
/// Profile dieses PCs (profiles.json im Datenordner). Der PC ist die Quelle: Profile entstehen im Launcher
/// (auch ohne Verbindung zum Panel) oder auf Wunsch des Admins und werden dem Panel gemeldet.
/// </summary>
internal sealed class AgentProfileStore
{
    private readonly string _file;
    private readonly Lock _lock = new();
    private LauncherProfile[] _profiles;

    public AgentProfileStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _file = Path.Combine(dataDirectory, "profiles.json");
        _profiles = Load(_file);
    }

    public event Action<LauncherProfile[]>? Changed;

    public LauncherProfile[] Current
    {
        get
        {
            lock (_lock)
                return _profiles;
        }
    }

    /// <summary>Neues Profil (Name und Farbe werden geprüft). null, wenn es nicht geht (Name, Höchstzahl, doppelt).</summary>
    public LauncherProfile? Add(string name, string? color, string? id = null, DateTimeOffset? createdAt = null)
    {
        if (LauncherProfile.ValidateName(name) is not null)
            return null;
        LauncherProfile profile;
        lock (_lock)
        {
            if (_profiles.Length >= LauncherProfile.MaxProfiles
                || _profiles.Any(p => string.Equals(p.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase) || p.Id == id))
                return null;
            profile = new LauncherProfile(id is { } given && given.StartsWith("profile:", StringComparison.Ordinal) ? given : LauncherProfile.NewId(),
                name.Trim(), ProfileColors.Normalize(color), createdAt ?? DateTimeOffset.UtcNow);
            Save([.. _profiles, profile]);
        }
        Changed?.Invoke(Current);
        return profile;
    }

    public bool Remove(string profileId)
    {
        lock (_lock)
        {
            if (_profiles.All(p => p.Id != profileId))
                return false;
            Save([.. _profiles.Where(p => p.Id != profileId)]);
        }
        Changed?.Invoke(Current);
        return true;
    }

    // Beim Entkoppeln bleiben die Profile (sie gehören den Leuten am PC), nur die Regeln fallen mit den Freigaben weg.
    private void Save(LauncherProfile[] profiles)
    {
        var temp = _file + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(profiles, MorniLanJsonContext.Default.LauncherProfileArray));
        File.Move(temp, _file, overwrite: true);
        _profiles = profiles;
    }

    private static LauncherProfile[] Load(string file)
    {
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize(File.ReadAllBytes(file), MorniLanJsonContext.Default.LauncherProfileArray) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
