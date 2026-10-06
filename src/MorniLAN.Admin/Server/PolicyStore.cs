using System.Collections.Concurrent;
using System.Text.Json;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Admin.Server;

/// <summary>
/// Freigaben je PC (policies\&lt;DeviceId&gt;.json) und welcher Stand dort zuletzt angekommen ist
/// (applied\&lt;DeviceId&gt;.txt). Das Panel ist die einzige Quelle der Freigaben.
/// </summary>
public sealed class PolicyStore
{
    private readonly string _policyDir;
    private readonly string _appliedDir;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, AppPolicy> _policies = new();
    private readonly Lock _writeLock = new();

    /// <summary>Freigaben oder Übertragungsstand eines PCs haben sich geändert.</summary>
    public event Action<Guid>? Changed;

    public PolicyStore(string dataDirectory, TimeProvider? time = null)
    {
        _policyDir = Path.Combine(dataDirectory, "policies");
        _appliedDir = Path.Combine(_policyDir, "applied");
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(_appliedDir);
    }

    public AppPolicy Get(Guid deviceId) => _policies.GetOrAdd(deviceId, Load);

    /// <summary>Ändert die Freigaben eines PCs und speichert sie. Gibt den neuen Stand zurück.</summary>
    public AppPolicy Update(Guid deviceId, Func<AppPolicy, DateTimeOffset, AppPolicy> change)
    {
        AppPolicy updated;
        lock (_writeLock)
        {
            updated = change(Get(deviceId), _time.GetUtcNow());
            var file = PolicyFile(deviceId);
            var temp = file + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(updated, MorniLanJsonContext.Default.AppPolicy));
            File.Move(temp, file, overwrite: true);
            _policies[deviceId] = updated;
        }
        Changed?.Invoke(deviceId);
        return updated;
    }

    /// <summary>Stand, den der PC zuletzt bestätigt hat (0 = noch nie).</summary>
    public long AppliedRevision(Guid deviceId)
    {
        var file = AppliedFile(deviceId);
        return File.Exists(file) && long.TryParse(File.ReadAllText(file), out var revision) ? revision : 0;
    }

    public void MarkApplied(Guid deviceId, long revision)
    {
        if (revision == AppliedRevision(deviceId))
            return;
        File.WriteAllText(AppliedFile(deviceId), revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Changed?.Invoke(deviceId);
    }

    /// <summary>Ist alles, was hier eingestellt ist, auf dem PC angekommen?</summary>
    public bool IsApplied(Guid deviceId) => AppliedRevision(deviceId) == Get(deviceId).Revision;

    public void Remove(Guid deviceId)
    {
        _policies.TryRemove(deviceId, out _);
        File.Delete(PolicyFile(deviceId));
        File.Delete(AppliedFile(deviceId));
        Changed?.Invoke(deviceId);
    }

    private AppPolicy Load(Guid deviceId)
    {
        var file = PolicyFile(deviceId);
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize(File.ReadAllBytes(file), MorniLanJsonContext.Default.AppPolicy) ?? AppPolicy.Default
                : AppPolicy.Default;
        }
        catch (JsonException)
        {
            return AppPolicy.Default;
        }
    }

    private string PolicyFile(Guid deviceId) => Path.Combine(_policyDir, $"{deviceId:N}.json");
    private string AppliedFile(Guid deviceId) => Path.Combine(_appliedDir, $"{deviceId:N}.txt");
}
