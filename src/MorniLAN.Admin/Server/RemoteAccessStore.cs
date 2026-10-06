using System.Collections.Concurrent;
using System.Text.Json;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Admin.Server;

/// <summary>
/// Fernzugriff je PC: die Einstellung „ohne Rückfrage erlauben“ (dauerhaft, remote-&lt;DeviceId&gt;.json) und der
/// zuletzt gemeldete Sitzungsstand (nur im Speicher, er ist nur kurz wichtig).
/// </summary>
public sealed class RemoteAccessStore
{
    private readonly string _dir;
    private readonly ConcurrentDictionary<Guid, RemoteAccessSettings> _settings = new();
    private readonly ConcurrentDictionary<Guid, RemoteSessionState> _states = new();

    public event Action<Guid>? Changed;

    public RemoteAccessStore(string dataDirectory)
    {
        _dir = Path.Combine(dataDirectory, "remote");
        Directory.CreateDirectory(_dir);
    }

    public RemoteAccessSettings Settings(Guid deviceId) => _settings.GetOrAdd(deviceId, Load);

    public RemoteSessionState State(Guid deviceId) => _states.GetValueOrDefault(deviceId, RemoteSessionState.Idle);

    public void SetAllowWithoutConsent(Guid deviceId, bool allow)
    {
        var settings = new RemoteAccessSettings(allow);
        _settings[deviceId] = settings;
        var file = File(deviceId);
        System.IO.File.WriteAllBytes(file + ".tmp", JsonSerializer.SerializeToUtf8Bytes(settings, MorniLanJsonContext.Default.RemoteAccessSettings));
        System.IO.File.Move(file + ".tmp", file, overwrite: true);
        Changed?.Invoke(deviceId);
    }

    public void Save(Guid deviceId, RemoteSessionState state)
    {
        _states[deviceId] = state;
        Changed?.Invoke(deviceId);
    }

    public void Remove(Guid deviceId)
    {
        _settings.TryRemove(deviceId, out _);
        _states.TryRemove(deviceId, out _);
        System.IO.File.Delete(File(deviceId));
        Changed?.Invoke(deviceId);
    }

    private RemoteAccessSettings Load(Guid deviceId)
    {
        try
        {
            return System.IO.File.Exists(File(deviceId))
                ? JsonSerializer.Deserialize(System.IO.File.ReadAllBytes(File(deviceId)), MorniLanJsonContext.Default.RemoteAccessSettings) ?? new()
                : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private string File(Guid deviceId) => Path.Combine(_dir, $"{deviceId:N}.json");
}
