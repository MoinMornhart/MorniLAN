using System.Collections.Concurrent;
using System.Text.Json;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Admin.Server;

/// <summary>Profile je PC, wie der PC sie zuletzt gemeldet hat (profiles\&lt;DeviceId&gt;.json). Quelle ist der PC.</summary>
public sealed class DeviceProfileStore
{
    private readonly string _dir;
    private readonly ConcurrentDictionary<Guid, LauncherProfile[]> _profiles = new();

    public event Action<Guid>? Changed;

    public DeviceProfileStore(string dataDirectory)
    {
        _dir = Path.Combine(dataDirectory, "profiles");
        Directory.CreateDirectory(_dir);
    }

    public LauncherProfile[] Get(Guid deviceId) => _profiles.GetOrAdd(deviceId, Load);

    public void Save(Guid deviceId, LauncherProfile[] profiles)
    {
        // Gemeldet vom PC: nur plausible Einträge übernehmen
        var clean = profiles
            .Where(p => p.Id.StartsWith("profile:", StringComparison.Ordinal) && LauncherProfile.ValidateName(p.Name) is null)
            .Take(LauncherProfile.MaxProfiles)
            .Select(p => p with { Name = p.Name.Trim(), Color = ProfileColors.Normalize(p.Color) })
            .ToArray();
        _profiles[deviceId] = clean;
        var file = File(deviceId);
        System.IO.File.WriteAllBytes(file + ".tmp", JsonSerializer.SerializeToUtf8Bytes(clean, MorniLanJsonContext.Default.LauncherProfileArray));
        System.IO.File.Move(file + ".tmp", file, overwrite: true);
        Changed?.Invoke(deviceId);
    }

    public void Remove(Guid deviceId)
    {
        _profiles.TryRemove(deviceId, out _);
        System.IO.File.Delete(File(deviceId));
        Changed?.Invoke(deviceId);
    }

    private LauncherProfile[] Load(Guid deviceId)
    {
        try
        {
            return System.IO.File.Exists(File(deviceId))
                ? JsonSerializer.Deserialize(System.IO.File.ReadAllBytes(File(deviceId)), MorniLanJsonContext.Default.LauncherProfileArray) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private string File(Guid deviceId) => Path.Combine(_dir, $"{deviceId:N}.json");
}

/// <summary>Eine Hilfe-Anfrage, wie sie im Panel erscheint.</summary>
public sealed record HelpNotice(Guid DeviceId, string MachineName, HelpRequest Request, DateTimeOffset ReceivedAt);

/// <summary>Offene Hilfe-Anfragen (nur im Speicher; sie sind ohnehin nur kurz wichtig).</summary>
public sealed class HelpInbox
{
    private readonly Lock _lock = new();
    private readonly List<HelpNotice> _open = [];

    /// <summary>Neue Anfrage oder eine wurde erledigt.</summary>
    public event Action<HelpNotice?>? Changed;

    public IReadOnlyList<HelpNotice> Open
    {
        get
        {
            lock (_lock)
                return [.. _open];
        }
    }

    public void Add(HelpNotice notice)
    {
        lock (_lock)
        {
            if (_open.Any(n => n.Request.Id == notice.Request.Id))
                return; // nach einem Verbindungsabbruch doppelt geschickt
            _open.Add(notice);
            if (_open.Count > 50)
                _open.RemoveAt(0);
        }
        Changed?.Invoke(notice);
    }

    public void Dismiss(string requestId)
    {
        lock (_lock)
            _open.RemoveAll(n => n.Request.Id == requestId);
        Changed?.Invoke(null);
    }
}
