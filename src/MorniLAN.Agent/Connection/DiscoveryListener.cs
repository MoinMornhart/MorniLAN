using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Agent.Connection;

internal sealed record SeenBeacon(DiscoveryBeacon Beacon, IPAddress Address, DateTimeOffset SeenAt);

/// <summary>Lauscht auf UDP-Beacons von Admin-Panels im LAN und merkt sich die zuletzt gesehenen.</summary>
internal sealed class DiscoveryListener(IOptions<AgentConnectionOptions> options, ILogger<DiscoveryListener> logger)
    : BackgroundService
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, SeenBeacon> _seen = new();
    private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Beacons der letzten 30 Sekunden, neueste zuerst.</summary>
    public IReadOnlyList<SeenBeacon> Recent()
    {
        var cutoff = DateTimeOffset.UtcNow - MaxAge;
        return [.. _seen.Values.Where(b => b.SeenAt >= cutoff).OrderByDescending(b => b.SeenAt)];
    }

    /// <summary>Wartet, bis ein neues Admin-Panel auftaucht (oder die Zeit abläuft).</summary>
    public async Task WaitForNewBeaconAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try { await Volatile.Read(ref _signal).Task.WaitAsync(timeout, cancellationToken); }
        catch (TimeoutException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableDiscovery)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var udp = NetworkInfo.OpenDiscoverySocket(options.Value.DiscoveryPort);
                logger.LogInformation("Suche Admin-Panel im LAN (UDP {Port})", options.Value.DiscoveryPort);
                using var querying = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var queries = SendQueriesAsync(udp, querying.Token);
                try
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        UdpReceiveResult received;
                        try { received = await udp.ReceiveAsync(stoppingToken); }
                        catch (SocketException) when (!stoppingToken.IsCancellationRequested) { continue; }
                        if (DiscoveryBeacon.TryParse(received.Buffer, out var beacon))
                            Remember(new SeenBeacon(beacon, received.RemoteEndPoint.Address, DateTimeOffset.UtcNow));
                    }
                }
                finally
                {
                    await querying.CancelAsync();
                    await queries;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex)
            {
                logger.LogWarning("LAN-Suche nicht möglich ({Error}), neuer Versuch in 30 s", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>
    /// Fragt regelmäßig selbst nach einem Panel. Die Antwort kommt direkt an diesen Socket, das
    /// klappt auch, wenn der Router die Beacons vom Kabel-LAN nicht ins WLAN weiterreicht.
    /// </summary>
    private async Task SendQueriesAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        var query = DiscoveryBeacon.Query.ToArray();
        var failed = new HashSet<string>();
        using var timer = new PeriodicTimer(options.Value.DiscoveryQueryInterval);
        do
        {
            var targets = options.Value.DiscoveryQueryTargets
                          ?? [.. NetworkInfo.BroadcastTargets().Select(a => new IPEndPoint(a, options.Value.DiscoveryPort))];
            foreach (var target in targets)
            {
                try
                {
                    await udp.SendAsync(query, target, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException ex)
                {
                    if (failed.Add($"{target}|{ex.SocketErrorCode}"))
                        logger.LogWarning("Suchanfrage an {Target} fehlgeschlagen: {Error}", target, ex.Message);
                }
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                    return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        } while (true);
    }

    private void Remember(SeenBeacon seen)
    {
        // Ein Panel mit mehreren Netzwerkkarten kommt über mehrere Adressen an, jede wird einzeln gemerkt.
        var key = $"{seen.Beacon.Fingerprint}|{seen.Address}|{seen.Beacon.Port}";
        var isNew = !_seen.ContainsKey(key);
        _seen[key] = seen;
        if (!isNew)
            return;
        logger.LogInformation("Admin-Panel {Name} im LAN gefunden: {Address}:{Port}", seen.Beacon.Name, seen.Address,
            seen.Beacon.Port);
        Interlocked.Exchange(ref _signal, new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }
}
