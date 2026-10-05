using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorniLAN.Agent.Connection;

/// <summary>Das Admin-Panel, mit dem dieser PC gekoppelt ist (Zertifikat-Pin + bekannte Adressen).</summary>
internal sealed record PinnedAdmin(
    string Fingerprint,
    string Name,
    int Port,
    string[] Endpoints,
    string? LastEndpoint,
    DateTimeOffset PairedAt);

internal sealed record AgentState(Guid DeviceId, PinnedAdmin? Admin = null);

/// <summary>Speichert <see cref="AgentState"/> in agent-state.json im Datenordner.</summary>
internal sealed class AgentStateStore
{
    private readonly Lock _lock = new();
    private readonly string _file;
    private AgentState _current;

    public AgentStateStore(string dataDirectory)
    {
        _file = Path.Combine(dataDirectory, "agent-state.json");
        AgentState? loaded = null;
        if (File.Exists(_file))
        {
            try { loaded = JsonSerializer.Deserialize(File.ReadAllBytes(_file), AgentJsonContext.Default.AgentState); }
            catch (JsonException) { /* beschädigt → neu anfangen */ }
        }

        if (loaded is null || loaded.DeviceId == Guid.Empty)
        {
            _current = new AgentState(Guid.NewGuid());
            Save(_current);
        }
        else
        {
            _current = loaded;
        }
    }

    public AgentState Current
    {
        get { lock (_lock) return _current; }
    }

    public AgentState Update(Func<AgentState, AgentState> change)
    {
        lock (_lock)
        {
            var next = change(_current);
            if (next != _current)
            {
                Save(next);
                _current = next;
            }
            return next;
        }
    }

    private void Save(AgentState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp = _file + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(state, AgentJsonContext.Default.AgentState));
        File.Move(temp, _file, overwrite: true);
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AgentState))]
internal sealed partial class AgentJsonContext : JsonSerializerContext;
