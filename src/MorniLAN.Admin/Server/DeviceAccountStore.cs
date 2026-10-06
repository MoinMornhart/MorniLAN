using System.Collections.Concurrent;
using System.Text.Json;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Admin.Server;

/// <summary>
/// Windows-Konten je PC (accounts\&lt;DeviceId&gt;.json) und der zuletzt gemeldete Sperren-Stand (nur im Speicher).
/// Der PC ist die Quelle der Konten; das Panel wählt in den Freigaben, welche eingeschränkt werden.
/// </summary>
public sealed class DeviceAccountStore
{
    private readonly string _dir;
    private readonly ConcurrentDictionary<Guid, LocalAccount[]> _accounts = new();
    private readonly ConcurrentDictionary<Guid, RestrictionState> _states = new();

    public event Action<Guid>? Changed;

    public DeviceAccountStore(string dataDirectory)
    {
        _dir = Path.Combine(dataDirectory, "accounts");
        Directory.CreateDirectory(_dir);
    }

    public LocalAccount[] Get(Guid deviceId) => _accounts.GetOrAdd(deviceId, Load);

    public RestrictionState State(Guid deviceId) => _states.GetValueOrDefault(deviceId, RestrictionState.Idle);

    public void Save(Guid deviceId, LocalAccount[] accounts)
    {
        var clean = accounts
            .Where(a => AccountSidLooksValid(a.Sid) && !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => a with { Name = a.Name.Trim() })
            .ToArray();
        _accounts[deviceId] = clean;
        var file = File(deviceId);
        System.IO.File.WriteAllBytes(file + ".tmp", JsonSerializer.SerializeToUtf8Bytes(clean, MorniLanJsonContext.Default.LocalAccountArray));
        System.IO.File.Move(file + ".tmp", file, overwrite: true);
        Changed?.Invoke(deviceId);
    }

    public void SaveState(Guid deviceId, RestrictionState state)
    {
        _states[deviceId] = state;
        Changed?.Invoke(deviceId);
    }

    public void Remove(Guid deviceId)
    {
        _accounts.TryRemove(deviceId, out _);
        _states.TryRemove(deviceId, out _);
        System.IO.File.Delete(File(deviceId));
        Changed?.Invoke(deviceId);
    }

    private static bool AccountSidLooksValid(string sid) => sid.StartsWith("S-1-5-21-", StringComparison.Ordinal);

    private LocalAccount[] Load(Guid deviceId)
    {
        try
        {
            return System.IO.File.Exists(File(deviceId))
                ? JsonSerializer.Deserialize(System.IO.File.ReadAllBytes(File(deviceId)), MorniLanJsonContext.Default.LocalAccountArray) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private string File(Guid deviceId) => Path.Combine(_dir, $"{deviceId:N}.json");
}
