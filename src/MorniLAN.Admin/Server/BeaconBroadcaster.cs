using System.Net;
using System.Net.Sockets;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Admin.Server;

/// <summary>
/// LAN-Suche auf Panel-Seite: sendet alle paar Sekunden einen <see cref="DiscoveryBeacon"/> an alle
/// Broadcast-Adressen und beantwortet Suchanfragen von Agents direkt (Unicast).
/// </summary>
internal sealed class BeaconBroadcaster(DiscoveryBeacon beacon, int port, ILogger logger)
{
    private readonly byte[] _payload = beacon.ToBytes();
    private readonly HashSet<string> _loggedErrors = [];
    private readonly HashSet<IPAddress> _answered = [];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        UdpClient udp;
        var canReceive = true;
        try
        {
            udp = NetworkInfo.OpenDiscoverySocket(port);
        }
        catch (SocketException ex)
        {
            // Port belegt o. ä.: dann wenigstens senden, Suchanfragen gehen verloren.
            logger.LogWarning("UDP {Port} nicht verfügbar ({Error}), beantworte keine Suchanfragen", port, ex.Message);
            udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            canReceive = false;
        }

        using (udp)
        {
            logger.LogInformation("LAN-Suche aktiv: Beacon alle {Seconds} s an {Targets} (UDP {Port})",
                ConnectionDefaults.BeaconInterval.TotalSeconds, string.Join(", ", NetworkInfo.BroadcastTargets()), port);
            await Task.WhenAll(
                SendLoopAsync(udp, cancellationToken),
                canReceive ? ReceiveLoopAsync(udp, cancellationToken) : Task.CompletedTask);
        }
    }

    private async Task SendLoopAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ConnectionDefaults.BeaconInterval);
        do
        {
            try
            {
                foreach (var target in NetworkInfo.BroadcastTargets())
                {
                    try
                    {
                        await udp.SendAsync(_payload, new IPEndPoint(target, port), cancellationToken);
                    }
                    catch (SocketException ex)
                    {
                        LogOnce($"{target}|{ex.SocketErrorCode}", "Beacon an {Target} fehlgeschlagen: {Error}", target, ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Nie still aussteigen: ein Fehler (z. B. beim Abfragen der Netzwerkkarten) wird
                // gemeldet, und im nächsten Takt geht es weiter.
                LogOnce(ex.GetType().Name, "Beacon-Fehler: {Error}", ex.Message);
            }
        } while (await WaitAsync(timer, cancellationToken));
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(cancellationToken);
                if (!DiscoveryBeacon.IsQuery(received.Buffer))
                    continue;
                await udp.SendAsync(_payload, received.RemoteEndPoint, cancellationToken);
                if (_answered.Add(received.RemoteEndPoint.Address))
                    logger.LogInformation("Suchanfrage von {Address} beantwortet", received.RemoteEndPoint.Address);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex)
            {
                LogOnce($"recv|{ex.SocketErrorCode}", "Suchanfrage nicht beantwortet: {Error}", ex.Message);
            }
        }
    }

    private void LogOnce(string key, string message, params object?[] args)
    {
        if (_loggedErrors.Add(key))
            logger.LogWarning(message, args);
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try { return await timer.WaitForNextTickAsync(cancellationToken); }
        catch (OperationCanceledException) { return false; }
    }
}
