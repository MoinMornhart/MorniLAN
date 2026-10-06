using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using MorniLAN.Agent.Inventory;
using MorniLAN.Agent.Platform;
using MorniLAN.Shared;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Security;

namespace MorniLAN.Agent.Connection;

/// <summary>
/// Hält die Verbindung zum Admin-Panel: Panel finden, per TLS verbinden (eigenes Client-Zertifikat,
/// Panel-Zertifikat gepinnt), bei Bedarf koppeln, danach Heartbeats senden. Bricht die Verbindung ab,
/// geht es mit Wartezeit von vorne los.
/// </summary>
internal sealed class AdminConnectionService(
    IOptions<AgentConnectionOptions> options,
    AgentStateStore store,
    AgentIdentity identity,
    SystemStatusCollector statusCollector,
    ILogger<AdminConnectionService> logger,
    DiscoveryListener? discovery = null,
    InventoryService? inventory = null,
    Updates.AgentUpdateService? updates = null,
    Policy.AgentPolicyStore? policy = null,
    Policy.LauncherCatalog? catalog = null,
    Policy.AgentProfileStore? profiles = null,
    Policy.LauncherInboxService? inbox = null,
    Restrictions.RestrictionService? restrictions = null) : BackgroundService
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);

    private readonly AgentConnectionOptions _options = options.Value;
    private readonly DeviceInfo _deviceInfo = new(store.Current.DeviceId, Environment.MachineName,
        WindowsEditionDetector.Detect(), VersionInfo.Version);
    private string? _pairingCode;
    private string? _adminName;

    public AgentLinkState State { get; private set; } = AgentLinkState.Searching;

    /// <summary>Aktueller Code in Normalform, solange nicht gekoppelt (für Tests und den Launcher).</summary>
    public string? PairingCode => Volatile.Read(ref _pairingCode);

    /// <summary>Name des Panels, mit dem der Agent gerade spricht (oder zuletzt gekoppelt war).</summary>
    public string? AdminName => Volatile.Read(ref _adminName) ?? store.Current.Admin?.Name;

    /// <summary>Momentaufnahme für den lokalen Statuskanal.</summary>
    public AgentLocalStatus LocalStatus() =>
        new(State, State == AgentLinkState.WaitingForPairing && PairingCode is { } code
                ? Shared.Connection.PairingCode.Format(code)
                : null,
            AdminName, _deviceInfo.MachineName, VersionInfo.Display, DateTimeOffset.UtcNow, catalog?.Current().Hash,
            inbox?.Help);

    public event Action<AgentLinkState>? StateChanged;

    private enum SessionOutcome { Unreachable, Ended }

    private abstract record ServerEvent;
    private sealed record Approved(PairingApproval Approval) : ServerEvent;
    private sealed record Rejected(string Reason) : ServerEvent;
    private sealed record Unpaired : ServerEvent;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Geräte-ID {DeviceId}, Zertifikat {Fingerprint}", _deviceInfo.DeviceId,
            CertificateFingerprint.Short(identity.Fingerprint));
        if (store.Current.Admin is { } admin)
            logger.LogInformation("Gekoppelt mit Admin-Panel {Name}", admin.Name);

        var failures = 0;
        var announcedSearch = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            var plan = EndpointPlanner.Plan(store.Current, _options.AdminHost, _options.AdminPort,
                discovery?.Recent() ?? [], NetworkInfo.LocalIPv4());
            if (plan.Count == 0)
            {
                SetState(AgentLinkState.Searching);
                if (!announcedSearch)
                    logger.LogInformation("Kein Admin-Panel bekannt, warte auf LAN-Beacon …");
                announcedSearch = true;
                await WaitForBeaconAsync(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            var reached = false;
            foreach (var endpoint in plan)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;
                if (await RunSessionAsync(endpoint, stoppingToken) == SessionOutcome.Unreachable)
                    continue;
                reached = true;
                break;
            }

            SetState(AgentLinkState.Searching);
            failures = reached ? 0 : failures + 1;
            if (!reached && failures == 1)
                logger.LogWarning("Admin-Panel nicht erreichbar ({Targets}), versuche es weiter",
                    string.Join(", ", plan.Select(e => $"{e.Host}:{e.Port}")));
            await WaitForBeaconAsync(Backoff[Math.Min(failures, Backoff.Length - 1)], stoppingToken);
        }
    }

    private async Task<SessionOutcome> RunSessionAsync(AdminEndpoint endpoint, CancellationToken stoppingToken)
    {
        string? adminFingerprint = null;
        var events = Channel.CreateUnbounded<ServerEvent>();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = BuildConnection(endpoint, fp => adminFingerprint = fp);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        connection.On<PairingApproval>(nameof(IAgentClient.OnPairingApproved), a => events.Writer.TryWrite(new Approved(a)));
        connection.On<string>(nameof(IAgentClient.OnPairingRejected), r => events.Writer.TryWrite(new Rejected(r)));
        connection.On(nameof(IAgentClient.OnUnpaired), () => events.Writer.TryWrite(new Unpaired()));
        var refreshRequested = new SemaphoreSlim(0, 1);
        connection.On(nameof(IAgentClient.OnRefreshInventory), () =>
        {
            if (refreshRequested.CurrentCount == 0)
                refreshRequested.Release();
        });
        connection.On(nameof(IAgentClient.OnInstallUpdate), () => updates?.RequestNow());
        connection.On<LauncherProfile>(nameof(IAgentClient.OnCreateProfile), p =>
        {
            if (profiles?.Add(p.Name, p.Color, p.Id, p.CreatedAt) is { } added)
                logger.LogInformation("Profil „{Name}“ vom Admin angelegt", added.Name);
        });
        connection.On<string>(nameof(IAgentClient.OnDeleteProfile), id =>
        {
            if (profiles?.Remove(id) == true)
                logger.LogInformation("Profil {Id} vom Admin gelöscht", id);
        });
        connection.On(nameof(IAgentClient.OnApplyRestrictions), () => restrictions?.ApplyNow());
        connection.On<AppPolicy>(nameof(IAgentClient.OnPolicyChanged), async received =>
        {
            try { await ApplyPolicyAsync(connection, received, authoritative: false, refreshRequested, stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Freigaben nicht übernommen: {Error}", ex.Message);
            }
        });

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(ConnectTimeout);
            await connection.StartAsync(timeout.Token);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{Host}:{Port} ({Source}) nicht erreichbar: {Error}", endpoint.Host, endpoint.Port,
                endpoint.Source, ex.Message);
            return SessionOutcome.Unreachable;
        }

        try
        {
            logger.LogInformation("Verbunden mit {Host}:{Port} ({Source})", endpoint.Host, endpoint.Port, endpoint.Source);
            var hello = await connection.InvokeAsync<HelloResponse>(nameof(IAdminHub.Hello),
                new HelloRequest(_deviceInfo), stoppingToken);
            Volatile.Write(ref _adminName, hello.AdminName);

            if (hello.Status == HelloStatus.PairingRequired)
            {
                if (store.Current.Admin is not null)
                {
                    logger.LogWarning("Das Admin-Panel kennt diesen PC nicht mehr, neues Pairing nötig");
                    ForgetAdmin();
                }
                if (!await PairAsync(connection, endpoint, adminFingerprint!, hello, events.Reader, closed.Task,
                        stoppingToken))
                    return SessionOutcome.Ended;
                hello = await connection.InvokeAsync<HelloResponse>(nameof(IAdminHub.Hello),
                    new HelloRequest(_deviceInfo), stoppingToken);
                if (hello.Status != HelloStatus.Paired)
                    return SessionOutcome.Ended;
            }

            store.Update(s => s.Admin is null ? s : s with
            {
                Admin = s.Admin with { Name = hello.AdminName, Endpoints = hello.AdminEndpoints, LastEndpoint = endpoint.Host },
            });
            SetState(AgentLinkState.Online);
            logger.LogInformation("Online bei Admin-Panel {Name}", hello.AdminName);

            // Freigaben abholen: Was das Panel jetzt sagt, gilt (auch wenn es neu eingerichtet wurde)
            if (policy is not null)
            {
                try
                {
                    var current = await connection.InvokeAsync<AppPolicy>(nameof(IAdminHub.GetPolicy), stoppingToken);
                    await ApplyPolicyAsync(connection, current, authoritative: true, refreshRequested, stoppingToken);
                }
                catch (Microsoft.AspNetCore.SignalR.HubException ex)
                {
                    logger.LogInformation("Panel liefert keine Freigaben (ältere Version?): {Error}", ex.Message);
                }
            }

            using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var inventorySync = inventory is null
                ? Task.CompletedTask
                : SyncInventoryAsync(connection, inventory, refreshRequested, session.Token);

            // Update-Stand ans Panel: einmal jetzt, danach bei jeder Änderung
            void ReportUpdate(Shared.Updates.AgentUpdateState state) =>
                _ = connection.InvokeAsync(nameof(IAdminHub.ReportUpdateState), state, session.Token)
                    .ContinueWith(t => logger.LogDebug("Update-Stand nicht gemeldet: {Error}", t.Exception?.GetBaseException().Message),
                        TaskContinuationOptions.OnlyOnFaulted);
            if (updates is not null)
            {
                updates.StateChanged += ReportUpdate;
                ReportUpdate(updates.State);
            }

            // Profile und Hilfe-Anfragen: jetzt einmal, danach bei jeder Änderung. Ältere Panels kennen das nicht.
            void ReportProfiles(LauncherProfile[] current) =>
                _ = connection.InvokeAsync(nameof(IAdminHub.ReportProfiles), current, session.Token)
                    .ContinueWith(t => logger.LogDebug("Profile nicht gemeldet: {Error}", t.Exception?.GetBaseException().Message),
                        TaskContinuationOptions.OnlyOnFaulted);
            void SendHelp(HelpRequest request) =>
                _ = connection.InvokeAsync(nameof(IAdminHub.RequestHelp), request, session.Token)
                    .ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                            inbox?.MarkDelivered(request.Id);
                        else
                            logger.LogInformation("Hilfe-Anfrage noch nicht angekommen: {Error}", t.Exception?.GetBaseException().Message);
                    }, TaskScheduler.Default);
            if (profiles is not null)
            {
                profiles.Changed += ReportProfiles;
                ReportProfiles(profiles.Current);
            }
            if (inbox is not null)
            {
                inbox.HelpRequested += SendHelp;
                if (inbox.PendingHelp is { } waiting)
                    SendHelp(waiting);
            }

            // Konten und Sperren-Stand ans Panel: jetzt und bei jeder Änderung
            void ReportAccounts(IReadOnlyList<LocalAccount> accounts) =>
                _ = connection.InvokeAsync(nameof(IAdminHub.ReportAccounts), accounts.ToArray(), session.Token)
                    .ContinueWith(t => logger.LogDebug("Konten nicht gemeldet: {Error}", t.Exception?.GetBaseException().Message),
                        TaskContinuationOptions.OnlyOnFaulted);
            void ReportRestrictions(RestrictionState state) =>
                _ = connection.InvokeAsync(nameof(IAdminHub.ReportRestrictionState), state, session.Token)
                    .ContinueWith(t => logger.LogDebug("Sperren-Stand nicht gemeldet: {Error}", t.Exception?.GetBaseException().Message),
                        TaskContinuationOptions.OnlyOnFaulted);
            if (restrictions is not null)
            {
                restrictions.AccountsChanged += ReportAccounts;
                restrictions.StateChanged += ReportRestrictions;
                ReportAccounts(restrictions.Accounts);
                ReportRestrictions(restrictions.State);
            }
            try
            {
                await HeartbeatLoopAsync(connection, events.Reader, closed.Task, stoppingToken);
            }
            finally
            {
                if (profiles is not null)
                    profiles.Changed -= ReportProfiles;
                if (inbox is not null)
                    inbox.HelpRequested -= SendHelp;
                if (restrictions is not null)
                {
                    restrictions.AccountsChanged -= ReportAccounts;
                    restrictions.StateChanged -= ReportRestrictions;
                }
                if (updates is not null)
                    updates.StateChanged -= ReportUpdate;
                await session.CancelAsync();
                await inventorySync.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Verbindung zu {Host} beendet: {Error}", endpoint.Host, ex.Message);
        }
        return SessionOutcome.Ended;
    }

    /// <summary>
    /// Schickt die Programmliste nach dem Verbinden, danach alle 15 Minuten oder auf Wunsch des Admins,
    /// aber nur, wenn sich etwas geändert hat. Bilder gehen nur einmal raus: das Panel sagt, welche ihm fehlen.
    /// Fehler hier beenden nie die Verbindung, die Heartbeats laufen weiter.
    /// </summary>
    private async Task SyncInventoryAsync(HubConnection connection, InventoryService service, SemaphoreSlim refreshRequested,
        CancellationToken cancellationToken)
    {
        string? sentHash = null;
        var force = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var report = force
                    ? await service.RefreshAsync(cancellationToken)
                    : await service.GetAsync(ConnectionDefaults.InventoryInterval, cancellationToken);
                if (force || report.ContentHash != sentHash)
                {
                    var missing = await connection.InvokeAsync<string[]>(nameof(IAdminHub.ReportInventory), report, cancellationToken);
                    var uploaded = 0;
                    foreach (var hash in missing)
                    {
                        if (service.GetImage(hash) is not { } image)
                            continue;
                        await connection.InvokeAsync(nameof(IAdminHub.UploadImage), image, cancellationToken);
                        uploaded++;
                    }
                    sentHash = report.ContentHash;
                    logger.LogInformation("Programmliste an das Panel geschickt ({Count} Einträge, {Images} Bilder)",
                        report.Apps.Length, uploaded);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Programmliste konnte nicht übertragen werden: {Error}", ex.Message);
            }

            force = await refreshRequested.WaitAsync(ConnectionDefaults.InventoryInterval, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> PairAsync(HubConnection connection, AdminEndpoint endpoint, string adminFingerprint,
        HelloResponse hello, ChannelReader<ServerEvent> events, Task closed, CancellationToken stoppingToken)
    {
        var code = PairingCodeOrNew();
        var transcript = new PairingTranscript(_deviceInfo.DeviceId, identity.Fingerprint, adminFingerprint);
        var key = await Task.Run(() => PairingProof.DeriveKey(code, transcript), stoppingToken);
        var proof = Convert.ToBase64String(PairingProof.Sign(key, PairingRole.Agent, transcript));

        await connection.InvokeAsync(nameof(IAdminHub.RequestPairing), new PairingRequest(_deviceInfo, proof),
            stoppingToken);
        SetState(AgentLinkState.WaitingForPairing);
        logger.LogWarning("PAIRING-CODE: {Code}  ← im Admin-Panel auf \"{Admin}\" eingeben",
            Shared.Connection.PairingCode.Format(code), hello.AdminName);

        var next = await NextEventAsync(events, closed, stoppingToken);
        switch (next)
        {
            case Approved approved when PairingProof.Verify(key, PairingRole.Admin, transcript, approved.Approval.AdminProof):
                await connection.InvokeAsync<HelloResponse>(nameof(IAdminHub.ConfirmPairing), stoppingToken);
                store.Update(s => s with
                {
                    Admin = new PinnedAdmin(adminFingerprint, hello.AdminName, endpoint.Port, hello.AdminEndpoints,
                        endpoint.Host, DateTimeOffset.UtcNow),
                });
                Volatile.Write(ref _pairingCode, null);
                logger.LogInformation("Gekoppelt mit Admin-Panel {Name} (Zertifikat {Fingerprint})", hello.AdminName,
                    CertificateFingerprint.Short(adminFingerprint));
                return true;
            case Approved:
                // Das Panel kennt den Code nicht → jemand hat sich dazwischengeschaltet.
                logger.LogError("Gegenbeweis des Admin-Panels ist falsch, Pairing abgebrochen (möglicher Angriff)");
                NewPairingCode();
                return false;
            case Rejected rejected:
                logger.LogWarning("Pairing abgelehnt: {Reason}. Neuer Code wird erzeugt.", rejected.Reason);
                NewPairingCode();
                return false;
            default:
                return false;
        }
    }

    private async Task HeartbeatLoopAsync(HubConnection connection, ChannelReader<ServerEvent> events, Task closed,
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval);
        var nextEvent = NextEventAsync(events, closed, stoppingToken);
        do
        {
            await connection.InvokeAsync(nameof(IAdminHub.Heartbeat), statusCollector.Collect(_deviceInfo.DeviceId),
                stoppingToken);
            var tick = timer.WaitForNextTickAsync(stoppingToken).AsTask();
            if (await Task.WhenAny(tick, nextEvent) == tick)
                continue;
            if (await nextEvent is Unpaired)
            {
                logger.LogWarning("Der Admin hat diesen PC entfernt, neues Pairing nötig");
                ForgetAdmin();
            }
            return;
        } while (!stoppingToken.IsCancellationRequested);
    }

    /// <summary>Nächstes Server-Ereignis, oder null wenn die Verbindung zu ist.</summary>
    private static async Task<ServerEvent?> NextEventAsync(ChannelReader<ServerEvent> events, Task closed,
        CancellationToken stoppingToken)
    {
        var read = events.ReadAsync(stoppingToken).AsTask();
        return await Task.WhenAny(read, closed) == read ? await read : null;
    }

    private HubConnection BuildConnection(AdminEndpoint endpoint, Action<string> onAdminCertificate)
    {
        var uri = new UriBuilder(Uri.UriSchemeHttps, endpoint.Host, endpoint.Port, ConnectionDefaults.HubPath).Uri;
        return new HubConnectionBuilder()
            .WithUrl(uri, http =>
            {
                http.SkipNegotiation = true;
                http.Transports = HttpTransportType.WebSockets;
                http.WebSocketConfiguration = ws =>
                {
                    ws.ClientCertificates = new X509CertificateCollection { identity.Certificate };
                    ws.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    {
                        // Kein CA-Check: Vertrauen entsteht nur über Pinning bzw. den Pairing-Beweis.
                        if (certificate is null)
                            return false;
                        var fingerprint = CertificateFingerprint.Of(certificate);
                        if (endpoint.ExpectedFingerprint is { } expected && !CertificateFingerprint.AreEqual(fingerprint, expected))
                            return false;
                        onAdminCertificate(fingerprint);
                        return true;
                    };
                };
            })
            .AddJsonProtocol(o => SignalRJson.Configure(o.PayloadSerializerOptions))
            .WithServerTimeout(ConnectionDefaults.OfflineAfter)
            .WithKeepAliveInterval(ConnectionDefaults.HeartbeatInterval)
            .Build();
    }

    /// <summary>Wartet die Zeit ab, endet aber früher, sobald im LAN ein neues Panel auftaucht.</summary>
    private async Task WaitForBeaconAsync(TimeSpan timeout, CancellationToken stoppingToken)
    {
        var wait = discovery?.WaitForNewBeaconAsync(timeout, stoppingToken) ?? Task.Delay(timeout, stoppingToken);
        await wait.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private void ForgetAdmin()
    {
        store.Update(s => s with { Admin = null });
        policy?.Reset();
        NewPairingCode();
    }

    /// <summary>
    /// Freigaben übernehmen und dem Panel melden, welcher Stand jetzt gilt. Sind eigene Einträge dazugekommen
    /// oder weggefallen, geht die Programmliste neu ans Panel (mit Icon aus der EXE).
    /// </summary>
    private async Task ApplyPolicyAsync(HubConnection connection, AppPolicy received, bool authoritative,
        SemaphoreSlim refreshRequested, CancellationToken cancellationToken)
    {
        if (policy is null)
            return;
        var before = policy.Current.CustomApps;
        if (policy.Apply(received, authoritative))
        {
            logger.LogInformation("Freigaben übernommen (Stand {Revision}, {Rules} Ausnahmen, {Custom} eigene Einträge)",
                received.Revision, received.Rules.Length, received.CustomApps.Length);
            if (!before.SequenceEqual(received.CustomApps) && refreshRequested.CurrentCount == 0)
                refreshRequested.Release();
        }
        await connection.InvokeAsync(nameof(IAdminHub.ReportPolicyApplied), policy.Current.Revision, cancellationToken);
    }

    private string PairingCodeOrNew() => PairingCode ?? NewPairingCode();

    private string NewPairingCode()
    {
        var code = Shared.Connection.PairingCode.Generate();
        Volatile.Write(ref _pairingCode, code);
        return code;
    }

    private void SetState(AgentLinkState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke(state);
    }
}

/// <summary>Zertifikat des Agents (DPAPI-geschützt im Datenordner).</summary>
internal sealed class AgentIdentity(X509Certificate2 certificate)
{
    public X509Certificate2 Certificate { get; } = certificate;
    public string Fingerprint { get; } = CertificateFingerprint.Of(certificate);

    public static AgentIdentity LoadOrCreate(string dataDirectory, ILogger logger)
    {
        var certificate = CertificateIdentityStore.LoadOrCreate(Path.Combine(dataDirectory, "agent-identity.bin"),
            $"MorniLAN Agent {Environment.MachineName}", out var created);
        if (created)
            logger.LogInformation("Neues Agent-Zertifikat erzeugt");
        return new AgentIdentity(certificate);
    }
}
