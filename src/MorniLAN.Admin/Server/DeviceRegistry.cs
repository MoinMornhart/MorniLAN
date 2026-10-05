using System.Text.Json;
using System.Text.Json.Serialization;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Security;

namespace MorniLAN.Admin.Server;

/// <summary>Gekoppelter PC, so wie er dauerhaft gespeichert wird.</summary>
public sealed record PairedDevice(Guid DeviceId, string MachineName, string Fingerprint, DateTimeOffset PairedAt);

/// <summary>Momentaufnahme eines PCs für die Oberfläche.</summary>
public sealed record DeviceSnapshot(
    PairedDevice Device,
    DeviceInfo? Info,
    DeviceStatus? Status,
    bool Connected,
    DateTimeOffset? LastSeen,
    string? RemoteAddress)
{
    public DevicePresence PresenceAt(DateTimeOffset now) => PresenceRules.Evaluate(Connected, LastSeen, now);
}

/// <summary>
/// Gekoppelte PCs (in devices.json) plus ihr Laufzeitzustand. Thread-sicher,
/// <see cref="Changed"/> kommt auf beliebigen Threads.
/// </summary>
public sealed class DeviceRegistry
{
    private sealed class Entry(PairedDevice device)
    {
        public PairedDevice Device { get; set; } = device;
        public DeviceInfo? Info { get; set; }
        public DeviceStatus? Status { get; set; }
        public string? ConnectionId { get; set; }
        public DateTimeOffset? LastSeen { get; set; }
        public string? RemoteAddress { get; set; }

        public DeviceSnapshot ToSnapshot() =>
            new(Device, Info, Status, ConnectionId is not null, LastSeen, RemoteAddress);
    }

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Entry> _devices = [];
    private readonly string _file;

    public event Action? Changed;

    public DeviceRegistry(string dataDirectory)
    {
        _file = Path.Combine(dataDirectory, "devices.json");
        if (!File.Exists(_file))
            return;
        var stored = JsonSerializer.Deserialize(File.ReadAllBytes(_file), AdminJsonContext.Default.ListPairedDevice);
        foreach (var device in stored ?? [])
            _devices[device.DeviceId] = new Entry(device);
    }

    public IReadOnlyList<DeviceSnapshot> Snapshot()
    {
        lock (_lock)
            return [.. _devices.Values.Select(e => e.ToSnapshot()).OrderBy(s => s.Device.MachineName)];
    }

    public PairedDevice? FindByFingerprint(string fingerprint)
    {
        lock (_lock)
            return _devices.Values.Select(e => e.Device)
                .FirstOrDefault(d => CertificateFingerprint.AreEqual(d.Fingerprint, fingerprint));
    }

    public bool IsPaired(Guid deviceId, string fingerprint)
    {
        lock (_lock)
            return _devices.TryGetValue(deviceId, out var e)
                && CertificateFingerprint.AreEqual(e.Device.Fingerprint, fingerprint);
    }

    /// <summary>Neu koppeln. Ein vorhandener Eintrag mit gleicher DeviceId wird ersetzt (z. B. neues Zertifikat).</summary>
    public void Add(PairedDevice device)
    {
        lock (_lock)
        {
            _devices[device.DeviceId] = new Entry(device);
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Entfernt den PC und liefert die offene Verbindung, falls er gerade verbunden ist.</summary>
    public bool Remove(Guid deviceId, out string? connectionId)
    {
        lock (_lock)
        {
            connectionId = null;
            if (!_devices.Remove(deviceId, out var entry))
                return false;
            connectionId = entry.ConnectionId;
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    public void MarkConnected(Guid deviceId, string connectionId, DeviceInfo info, string? remoteAddress,
        DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var e))
                return;
            e.ConnectionId = connectionId;
            e.Info = info;
            e.RemoteAddress = remoteAddress;
            e.LastSeen = now;
            if (e.Device.MachineName != info.MachineName)
            {
                e.Device = e.Device with { MachineName = info.MachineName };
                Save();
            }
        }
        Changed?.Invoke();
    }

    public void MarkHeartbeat(Guid deviceId, string connectionId, DeviceStatus status, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var e) || e.ConnectionId != connectionId)
                return;
            e.Status = status;
            e.LastSeen = now;
        }
        Changed?.Invoke();
    }

    public void MarkDisconnected(Guid deviceId, string connectionId)
    {
        lock (_lock)
        {
            // Nur die aktuelle Verbindung zählt: Nach einem schnellen Reconnect darf das
            // verspätete Disconnect der alten Verbindung den PC nicht offline setzen.
            if (!_devices.TryGetValue(deviceId, out var e) || e.ConnectionId != connectionId)
                return;
            e.ConnectionId = null;
        }
        Changed?.Invoke();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var list = _devices.Values.Select(e => e.Device).ToList();
        var temp = _file + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(list, AdminJsonContext.Default.ListPairedDevice));
        File.Move(temp, _file, overwrite: true);
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(List<PairedDevice>))]
internal sealed partial class AdminJsonContext : JsonSerializerContext;
