using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Security;
using Serilog;

namespace MorniLAN.Admin.Server;

public enum PairingSubmitResult
{
    Approved,
    InvalidFormat,
    WrongCode,
    TooManyAttempts,
    NotFound,
}

/// <summary>Offene Pairing-Anfrage, wie sie in der Oberfläche erscheint.</summary>
public sealed record PendingPairingSnapshot(
    Guid RequestId,
    DeviceInfo Device,
    string AgentFingerprint,
    string? RemoteAddress,
    int FailedAttempts,
    DateTimeOffset RequestedAt);

/// <summary>
/// Ablauf: Agent schickt <see cref="PairingRequest"/> → Admin tippt den Code ein → Panel prüft den
/// Agent-Beweis und schickt seinen Gegenbeweis → Agent prüft und ruft ConfirmPairing → erst dann
/// wird das Gerät gespeichert.
/// </summary>
public sealed class PairingCoordinator(DeviceRegistry registry, AdminIdentity identity, TimeProvider time)
{
    private sealed class Pending
    {
        public required Guid RequestId { get; init; }
        public required string ConnectionId { get; init; }
        public required PairingRequest Request { get; init; }
        public required string AgentFingerprint { get; init; }
        public string? RemoteAddress { get; init; }
        public DateTimeOffset RequestedAt { get; init; }
        public int FailedAttempts { get; set; }
        public bool Approved { get; set; }

        public PairingTranscript Transcript(string adminFingerprint) =>
            new(Request.Device.DeviceId, AgentFingerprint, adminFingerprint);

        public PendingPairingSnapshot ToSnapshot() =>
            new(RequestId, Request.Device, AgentFingerprint, RemoteAddress, FailedAttempts, RequestedAt);
    }

    private readonly Lock _lock = new();
    private readonly List<Pending> _pending = [];

    /// <summary>Wird beim Serverstart gesetzt: liefert den Rückkanal zu einer Verbindung.</summary>
    internal Func<string, IAgentClient>? ClientResolver { get; set; }

    public event Action? Changed;

    public IReadOnlyList<PendingPairingSnapshot> Snapshot()
    {
        lock (_lock)
            return [.. _pending.Where(p => !p.Approved).Select(p => p.ToSnapshot())];
    }

    internal void Register(string connectionId, string agentFingerprint, PairingRequest request, string? remoteAddress)
    {
        lock (_lock)
        {
            _pending.RemoveAll(p => p.ConnectionId == connectionId || p.Request.Device.DeviceId == request.Device.DeviceId);
            _pending.Add(new Pending
            {
                RequestId = Guid.NewGuid(),
                ConnectionId = connectionId,
                Request = request,
                AgentFingerprint = agentFingerprint,
                RemoteAddress = remoteAddress,
                RequestedAt = time.GetUtcNow(),
            });
        }
        Changed?.Invoke();
    }

    public async Task<PairingSubmitResult> SubmitCodeAsync(Guid requestId, string code)
    {
        if (!PairingCode.TryNormalize(code, out var normalized))
            return PairingSubmitResult.InvalidFormat;

        Pending? pending;
        lock (_lock)
            pending = _pending.FirstOrDefault(p => p.RequestId == requestId && !p.Approved);
        if (pending is null)
            return PairingSubmitResult.NotFound;

        var transcript = pending.Transcript(identity.Fingerprint);
        // PBKDF2 kostet bewusst Rechenzeit, deshalb nicht auf dem UI-Thread.
        var key = await Task.Run(() => PairingProof.DeriveKey(normalized, transcript));

        if (!PairingProof.Verify(key, PairingRole.Agent, transcript, pending.Request.AgentProof))
        {
            bool exhausted;
            lock (_lock)
            {
                pending.FailedAttempts++;
                exhausted = pending.FailedAttempts >= ConnectionDefaults.MaxPairingAttempts;
                if (exhausted)
                    _pending.Remove(pending);
            }
            Changed?.Invoke();
            Log.Warning("Falscher Pairing-Code für {Machine} ({Remote}), Versuch {Attempt} von {Max}",
                pending.Request.Device.MachineName, pending.RemoteAddress, pending.FailedAttempts,
                ConnectionDefaults.MaxPairingAttempts);
            if (!exhausted)
                return PairingSubmitResult.WrongCode;
            Log.Warning("Pairing-Anfrage von {Machine} nach zu vielen falschen Codes verworfen",
                pending.Request.Device.MachineName);
            await SendAsync(pending.ConnectionId, c => c.OnPairingRejected("Zu viele falsche Codes."));
            return PairingSubmitResult.TooManyAttempts;
        }

        lock (_lock)
            pending.Approved = true;
        Changed?.Invoke();
        Log.Information("Pairing-Code für {Machine} ({Remote}) richtig, warte auf Bestätigung des Agents",
            pending.Request.Device.MachineName, pending.RemoteAddress);
        var proof = Convert.ToBase64String(PairingProof.Sign(key, PairingRole.Admin, transcript));
        await SendAsync(pending.ConnectionId, c => c.OnPairingApproved(new PairingApproval(proof)));
        return PairingSubmitResult.Approved;
    }

    public async Task RejectAsync(Guid requestId)
    {
        Pending? pending;
        lock (_lock)
        {
            pending = _pending.FirstOrDefault(p => p.RequestId == requestId);
            if (pending is not null)
                _pending.Remove(pending);
        }
        if (pending is null)
            return;
        Changed?.Invoke();
        Log.Information("Pairing-Anfrage von {Machine} vom Admin abgelehnt", pending.Request.Device.MachineName);
        await SendAsync(pending.ConnectionId, c => c.OnPairingRejected("Vom Admin abgelehnt."));
    }

    /// <summary>Agent hat den Gegenbeweis akzeptiert → Gerät dauerhaft speichern.</summary>
    internal PairedDevice? Complete(string connectionId, string agentFingerprint)
    {
        Pending? pending;
        lock (_lock)
        {
            pending = _pending.FirstOrDefault(p => p.ConnectionId == connectionId && p.Approved
                && CertificateFingerprint.AreEqual(p.AgentFingerprint, agentFingerprint));
            if (pending is null)
                return null;
            _pending.Remove(pending);
        }
        var device = new PairedDevice(pending.Request.Device.DeviceId, pending.Request.Device.MachineName,
            pending.AgentFingerprint, time.GetUtcNow());
        registry.Add(device);
        Changed?.Invoke();
        return device;
    }

    internal void ConnectionClosed(string connectionId)
    {
        int removed;
        lock (_lock)
            removed = _pending.RemoveAll(p => p.ConnectionId == connectionId);
        if (removed > 0)
            Changed?.Invoke();
    }

    private async Task SendAsync(string connectionId, Func<IAgentClient, Task> send)
    {
        if (ClientResolver is { } resolve)
            await send(resolve(connectionId));
    }
}
