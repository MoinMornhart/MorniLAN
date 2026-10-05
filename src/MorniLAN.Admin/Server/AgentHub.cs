using Microsoft.AspNetCore.SignalR;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Security;

namespace MorniLAN.Admin.Server;

/// <summary>
/// Endpunkt für Agents. Jede Verbindung kommt per TLS mit Client-Zertifikat; dessen Fingerabdruck
/// ist die Identität des PCs. Ohne Pairing sind nur Hello und RequestPairing erlaubt.
/// </summary>
internal sealed class AgentHub(
    DeviceRegistry registry,
    PairingCoordinator pairing,
    AdminIdentity identity,
    TimeProvider time,
    ILogger<AgentHub> logger) : Hub<IAgentClient>, IAdminHub
{
    private const string FingerprintKey = "fp";
    private const string DeviceKey = "device";

    private string Fingerprint => (string)Context.Items[FingerprintKey]!;
    private string? RemoteAddress => NetworkInfo.Display(Context.GetHttpContext()?.Connection.RemoteIpAddress);

    public override Task OnConnectedAsync()
    {
        var certificate = Context.GetHttpContext()?.Connection.ClientCertificate;
        if (certificate is null)
        {
            logger.LogWarning("Verbindung ohne Client-Zertifikat von {Remote} abgewiesen", RemoteAddress);
            Context.Abort();
            return Task.CompletedTask;
        }
        Context.Items[FingerprintKey] = CertificateFingerprint.Of(certificate);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(DeviceKey, out var id) && id is Guid deviceId)
        {
            registry.MarkDisconnected(deviceId, Context.ConnectionId);
            logger.LogInformation("PC {DeviceId} getrennt", deviceId);
        }
        pairing.ConnectionClosed(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task<HelloResponse> Hello(HelloRequest request)
    {
        var device = request.Device;
        if (!registry.IsPaired(device.DeviceId, Fingerprint))
        {
            logger.LogInformation("Unbekannter PC {Machine} ({Remote}) meldet sich, Pairing nötig", device.MachineName,
                RemoteAddress);
            return Task.FromResult(Response(HelloStatus.PairingRequired));
        }
        return Task.FromResult(Accept(device));
    }

    public Task RequestPairing(PairingRequest request)
    {
        if (registry.IsPaired(request.Device.DeviceId, Fingerprint))
            throw new HubException("Dieser PC ist bereits gekoppelt.");
        logger.LogInformation("Pairing-Anfrage von {Machine} ({Remote})", request.Device.MachineName, RemoteAddress);
        pairing.Register(Context.ConnectionId, Fingerprint, request, RemoteAddress);
        return Task.CompletedTask;
    }

    public Task<HelloResponse> ConfirmPairing()
    {
        var device = pairing.Complete(Context.ConnectionId, Fingerprint)
                     ?? throw new HubException("Keine bestätigte Pairing-Anfrage für diese Verbindung.");
        logger.LogInformation("PC {Machine} ({DeviceId}) gekoppelt", device.MachineName, device.DeviceId);
        // Zum Zeitpunkt der Bestätigung kennen wir die DeviceInfo nur aus der Anfrage,
        // der Agent schickt danach ohnehin sofort einen Heartbeat.
        return Task.FromResult(Response(HelloStatus.Paired));
    }

    public Task Heartbeat(DeviceStatus status)
    {
        if (!Context.Items.TryGetValue(DeviceKey, out var id) || id is not Guid deviceId
            || !registry.IsPaired(deviceId, Fingerprint))
            throw new HubException("Nicht gekoppelt.");
        registry.MarkHeartbeat(deviceId, Context.ConnectionId, status with { DeviceId = deviceId }, time.GetUtcNow());
        return Task.CompletedTask;
    }

    /// <summary>Nach dem Pairing meldet sich der Agent erneut mit Hello, damit ist er online.</summary>
    private HelloResponse Accept(DeviceInfo device)
    {
        Context.Items[DeviceKey] = device.DeviceId;
        registry.MarkConnected(device.DeviceId, Context.ConnectionId, device, RemoteAddress, time.GetUtcNow());
        logger.LogInformation("PC {Machine} online ({Remote})", device.MachineName, RemoteAddress);
        return Response(HelloStatus.Paired);
    }

    private HelloResponse Response(HelloStatus status) =>
        new(status, identity.Name, NetworkInfo.AdminEndpoints());
}
