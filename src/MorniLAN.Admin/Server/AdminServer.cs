using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.SignalR;
using MorniLAN.Shared;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Security;
using Serilog;

namespace MorniLAN.Admin.Server;

/// <summary>Eigenes Zertifikat und Name des Admin-Panels.</summary>
public sealed class AdminIdentity(X509Certificate2 certificate, string name)
{
    public X509Certificate2 Certificate { get; } = certificate;
    public string Fingerprint { get; } = CertificateFingerprint.Of(certificate);
    public string Name { get; } = name;
}

public sealed class AdminServerOptions
{
    public string DataDirectory { get; init; } = AdminPaths.Data;

    /// <summary>null = alle Adressen (IPv4 und IPv6).</summary>
    public IPAddress? BindAddress { get; init; }

    /// <summary>0 = freien Port wählen (für Tests).</summary>
    public int Port { get; init; } = MorniLanConstants.AdminPort;

    public bool EnableDiscoveryBeacon { get; init; } = true;
    public int DiscoveryPort { get; init; } = MorniLanConstants.DiscoveryPort;
    public string Name { get; init; } = Environment.MachineName;
    public TimeProvider Time { get; init; } = TimeProvider.System;
}

/// <summary>
/// Kestrel + SignalR im Admin-Panel. Agents verbinden sich hierher (TLS mit Client-Zertifikat),
/// im LAN finden sie das Panel über den UDP-Beacon.
/// </summary>
public sealed class AdminServer : IAsyncDisposable
{
    private readonly AdminServerOptions _options;
    private readonly CancellationTokenSource _stopping = new();
    private WebApplication? _app;
    private Task? _beacon;

    public AdminServer(AdminServerOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.DataDirectory);
        var certificate = CertificateIdentityStore.LoadOrCreate(Path.Combine(options.DataDirectory, "admin-identity.bin"),
            $"MorniLAN Admin {options.Name}", out _);
        Identity = new AdminIdentity(certificate, options.Name);
        Registry = new DeviceRegistry(options.DataDirectory);
        Pairing = new PairingCoordinator(Registry, Identity, options.Time);
        Inventory = new InventoryStore(options.DataDirectory);
    }

    public AdminIdentity Identity { get; }
    public DeviceRegistry Registry { get; }
    public PairingCoordinator Pairing { get; }
    public InventoryStore Inventory { get; }

    /// <summary>Tatsächlicher Port nach dem Start.</summary>
    public int Port { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            if (_options.BindAddress is { } address)
                kestrel.Listen(address, _options.Port, UseTls);
            else
                kestrel.ListenAnyIP(_options.Port, UseTls);
        });

        builder.Services.AddSingleton(Identity);
        builder.Services.AddSingleton(Registry);
        builder.Services.AddSingleton(Pairing);
        builder.Services.AddSingleton(Inventory);
        builder.Services.AddSingleton(_options.Time);
        builder.Services.AddSignalR(o =>
            {
                o.KeepAliveInterval = ConnectionDefaults.HeartbeatInterval;
                o.ClientTimeoutInterval = ConnectionDefaults.OfflineAfter;
                o.MaximumReceiveMessageSize = ConnectionDefaults.MaxMessageBytes;
                o.EnableDetailedErrors = false;
            })
            .AddJsonProtocol(o => SignalRJson.Configure(o.PayloadSerializerOptions));

        var app = builder.Build();
        app.MapHub<AgentHub>(ConnectionDefaults.HubPath);

        var hub = app.Services.GetRequiredService<IHubContext<AgentHub, IAgentClient>>();
        Pairing.ClientResolver = connectionId => hub.Clients.Client(connectionId);

        await app.StartAsync(cancellationToken);
        _app = app;

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        Port = new Uri(addresses.First().Replace("[::]", "localhost")).Port;
        Log.Information("Admin-Server läuft auf Port {Port}, Zertifikat {Fingerprint}", Port,
            CertificateFingerprint.Short(Identity.Fingerprint));

        if (_options.EnableDiscoveryBeacon)
        {
            var beacon = DiscoveryBeacon.ForAdmin(Identity.Name, Port, Identity.Fingerprint);
            var logger = app.Services.GetRequiredService<ILogger<BeaconBroadcaster>>();
            _beacon = new BeaconBroadcaster(beacon, _options.DiscoveryPort, logger).RunAsync(_stopping.Token);
        }
    }

    /// <summary>PC entfernen. Ist er gerade verbunden, erfährt der Agent es sofort.</summary>
    public async Task UnpairAsync(Guid deviceId)
    {
        if (!Registry.Remove(deviceId, out var connectionId))
            return;
        Log.Information("PC {DeviceId} vom Admin entkoppelt", deviceId);
        if (connectionId is null || _app is null)
            return;
        var hub = _app.Services.GetRequiredService<IHubContext<AgentHub, IAgentClient>>();
        await hub.Clients.Client(connectionId).OnUnpaired();
    }

    /// <summary>Den Agent bitten, seine Programmliste sofort neu einzulesen und zu schicken.</summary>
    public async Task<bool> RequestInventoryRefreshAsync(Guid deviceId)
    {
        if (Registry.ConnectionIdOf(deviceId) is not { } connectionId || _app is null)
            return false;
        var hub = _app.Services.GetRequiredService<IHubContext<AgentHub, IAgentClient>>();
        await hub.Clients.Client(connectionId).OnRefreshInventory();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_beacon is not null)
            await _beacon;
        if (_app is not null)
        {
            await _app.StopAsync(TimeSpan.FromSeconds(3));
            await _app.DisposeAsync();
        }
        Identity.Certificate.Dispose();
        _stopping.Dispose();
    }

    private void UseTls(ListenOptions listen) =>
        listen.UseHttps(https =>
        {
            https.ServerCertificate = Identity.Certificate;
            // Jeder Agent muss ein Zertifikat zeigen. Ob es bekannt ist, entscheidet der Hub (Pinning).
            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            https.AllowAnyClientCertificate();
            https.CheckCertificateRevocation = false;
        });
}
