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
    InventoryStore inventory,
    PolicyStore policies,
    DeviceProfileStore profiles,
    DeviceAccountStore accounts,
    RemoteAccessStore remote,
    HelpInbox help,
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
        var deviceId = RequirePairedDevice();
        registry.MarkHeartbeat(deviceId, Context.ConnectionId, status with { DeviceId = deviceId }, time.GetUtcNow());
        return Task.CompletedTask;
    }

    public Task<string[]> ReportInventory(InventoryReport report)
    {
        var deviceId = RequirePairedDevice();
        if (report.Apps.Length > 5000)
            throw new HubException("Programmliste zu groß.");
        inventory.Save(deviceId, report);
        var missing = inventory.MissingImages(report);
        logger.LogInformation("Programmliste von {DeviceId}: {Count} Einträge, {Missing} Bilder angefordert", deviceId,
            report.Apps.Length, missing.Length);
        return Task.FromResult(missing);
    }

    public Task UploadImage(AppImage image)
    {
        RequirePairedDevice();
        if (!inventory.TrySaveImage(image))
            logger.LogWarning("Bild {Hash} abgelehnt (Hash, Größe oder Typ passt nicht)", image.Hash);
        return Task.CompletedTask;
    }

    public Task ReportUpdateState(MorniLAN.Shared.Updates.AgentUpdateState state)
    {
        var deviceId = RequirePairedDevice();
        var message = state.Message.Length <= 300 ? state.Message : state.Message[..300];
        registry.MarkUpdateState(deviceId, Context.ConnectionId, state with { Message = message });
        return Task.CompletedTask;
    }

    public Task<AppPolicy> GetPolicy() => Task.FromResult(policies.Get(RequirePairedDevice()));

    public Task ReportPolicyApplied(long revision)
    {
        var deviceId = RequirePairedDevice();
        policies.MarkApplied(deviceId, revision);
        logger.LogInformation("PC {DeviceId} wendet Freigaben-Stand {Revision} an", deviceId, revision);
        return Task.CompletedTask;
    }

    public Task ReportProfiles(LauncherProfile[] reported)
    {
        var deviceId = RequirePairedDevice();
        if (reported.Length > LauncherProfile.MaxProfiles * 2)
            throw new HubException("Zu viele Profile.");
        profiles.Save(deviceId, reported);
        return Task.CompletedTask;
    }

    public Task RequestHelp(HelpRequest request)
    {
        var deviceId = RequirePairedDevice();
        var name = registry.Snapshot().FirstOrDefault(d => d.Device.DeviceId == deviceId) is { } d
            ? d.Info?.MachineName ?? d.Device.MachineName
            : "PC";
        var profile = request.ProfileName is { Length: > LauncherProfile.MaxNameLength } tooLong ? tooLong[..LauncherProfile.MaxNameLength] : request.ProfileName;
        help.Add(new HelpNotice(deviceId, name, request with { ProfileName = profile }, time.GetUtcNow()));
        logger.LogInformation("Hilfe angefordert auf {Machine} ({Profile})", name, profile ?? "ohne Profil");
        return Task.CompletedTask;
    }

    public Task ReportAccounts(LocalAccount[] reported)
    {
        var deviceId = RequirePairedDevice();
        if (reported.Length > 200)
            throw new HubException("Zu viele Konten.");
        accounts.Save(deviceId, reported);
        return Task.CompletedTask;
    }

    public Task ReportRestrictionState(RestrictionState state)
    {
        var deviceId = RequirePairedDevice();
        var message = state.Message.Length <= 300 ? state.Message : state.Message[..300];
        accounts.SaveState(deviceId, state with { Message = message });
        return Task.CompletedTask;
    }

    public Task ReportRemoteState(RemoteSessionState state)
    {
        var deviceId = RequirePairedDevice();
        var message = state.Message.Length <= 300 ? state.Message : state.Message[..300];
        remote.Save(deviceId, state with { Message = message });
        return Task.CompletedTask;
    }

    private Guid RequirePairedDevice() =>
        Context.Items.TryGetValue(DeviceKey, out var id) && id is Guid deviceId && registry.IsPaired(deviceId, Fingerprint)
            ? deviceId
            : throw new HubException("Nicht gekoppelt.");

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
