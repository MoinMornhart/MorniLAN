using System.Text.Json;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Agent.Policy;

/// <summary>
/// Freigaben dieses PCs, wie das Panel sie zuletzt geschickt hat (policy.json im Datenordner). Liegen lokal,
/// damit sie auch gelten, wenn das Panel nicht erreichbar ist. Nur neuere Stände werden übernommen.
/// </summary>
internal sealed class AgentPolicyStore
{
    private readonly string _file;
    private readonly Lock _lock = new();
    private AppPolicy _current;

    public AgentPolicyStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _file = Path.Combine(dataDirectory, "policy.json");
        _current = Load(_file) ?? AppPolicy.Default;
    }

    public AppPolicy Current
    {
        get
        {
            lock (_lock)
                return _current;
        }
    }

    /// <summary>Neue Freigaben gespeichert (eigene Einträge können dazugekommen sein).</summary>
    public event Action<AppPolicy>? Changed;

    /// <summary>
    /// Übernimmt einen Stand. Vom Panel nachgeschobene Änderungen nur, wenn sie neuer sind (Reihenfolge im Netz ist
    /// nicht garantiert). Der beim Verbinden abgefragte Stand (<paramref name="authoritative"/>) gilt immer, auch mit
    /// kleinerer Nummer, z. B. nachdem das Panel neu eingerichtet wurde. true, wenn sich etwas geändert hat.
    /// </summary>
    public bool Apply(AppPolicy policy, bool authoritative = false)
    {
        if (!IsPlausible(policy))
            return false;
        var data = JsonSerializer.SerializeToUtf8Bytes(policy, MorniLanJsonContext.Default.AppPolicy);
        lock (_lock)
        {
            var same = data.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(_current, MorniLanJsonContext.Default.AppPolicy));
            if (same || (!authoritative && policy.Revision <= _current.Revision))
                return false;
            var temp = _file + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, _file, overwrite: true);
            _current = policy;
        }
        Changed?.Invoke(policy);
        return true;
    }

    /// <summary>Der PC wurde vom Panel entfernt: ohne Admin gelten keine Freigaben mehr (alles frei).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            File.Delete(_file);
            _current = AppPolicy.Default;
        }
        Changed?.Invoke(AppPolicy.Default);
    }

    /// <summary>Das Panel ist vertrauenswürdig (gepinnt), aber Unsinn soll trotzdem nicht auf die Platte.</summary>
    private static bool IsPlausible(AppPolicy policy) =>
        policy.Rules.Length <= ConnectionDefaults.MaxPolicyRules
        && policy.CustomApps.Length <= ConnectionDefaults.MaxCustomApps
        && policy.CustomApps.All(c => CustomApp.Validate(c.Name, c.ExecutablePath) is null && c.Id.StartsWith("custom:", StringComparison.Ordinal));

    private static AppPolicy? Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize(File.ReadAllBytes(file), MorniLanJsonContext.Default.AppPolicy) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
