using System.Collections.Concurrent;
using MorniLAN.Shared.Models;

namespace MorniLAN.Admin.Server;

/// <summary>Ein Eintrag im Aktionsverlauf (ausstehend oder fertig).</summary>
public sealed record ActionEntry(Guid CommandId, Guid DeviceId, string Description, DateTimeOffset At, bool? Success, string? Message);

/// <summary>
/// Verlauf der Admin-Befehle je PC (nur im Speicher; sie sind nur kurz interessant). Das Panel zeigt, was gerade
/// läuft und ob es geklappt hat.
/// </summary>
public sealed class ActionLog
{
    private readonly Lock _lock = new();
    private readonly List<ActionEntry> _entries = [];

    public event Action? Changed;

    /// <summary>Befehl ist abgeschickt (noch ohne Ergebnis).</summary>
    public void Started(Guid deviceId, AdminCommand command)
    {
        lock (_lock)
        {
            _entries.Insert(0, new ActionEntry(command.CommandId, deviceId, command.Describe(), DateTimeOffset.Now, null, null));
            Trim();
        }
        Changed?.Invoke();
    }

    /// <summary>Ergebnis vom PC.</summary>
    public void Record(Guid deviceId, CommandResult result)
    {
        lock (_lock)
        {
            var index = _entries.FindIndex(e => e.CommandId == result.CommandId);
            var entry = (index >= 0 ? _entries[index] : new ActionEntry(result.CommandId, deviceId, result.Description, DateTimeOffset.Now, null, null))
                with { Success = result.Success, Message = result.Message, At = DateTimeOffset.Now };
            if (index >= 0)
                _entries[index] = entry;
            else
            {
                _entries.Insert(0, entry);
                Trim();
            }
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<ActionEntry> For(Guid deviceId)
    {
        lock (_lock)
            return [.. _entries.Where(e => e.DeviceId == deviceId)];
    }

    private void Trim()
    {
        if (_entries.Count > 100)
            _entries.RemoveRange(100, _entries.Count - 100);
    }
}
