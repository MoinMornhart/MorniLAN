using System.Net;
using System.Net.Sockets;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Admin.Server;

/// <summary>Sendet alle paar Sekunden einen <see cref="DiscoveryBeacon"/> an alle LAN-Broadcast-Adressen.</summary>
internal sealed class BeaconBroadcaster(DiscoveryBeacon beacon, int targetPort, ILogger logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        var payload = beacon.ToBytes();
        var lastError = string.Empty;
        using var timer = new PeriodicTimer(ConnectionDefaults.BeaconInterval);
        do
        {
            foreach (var target in Targets())
            {
                try
                {
                    await udp.SendAsync(payload, new IPEndPoint(target, targetPort), cancellationToken);
                }
                catch (SocketException ex) when (ex.Message != lastError)
                {
                    // Jeden Fehler nur einmal loggen, sonst läuft das Log alle 3 s voll.
                    lastError = ex.Message;
                    logger.LogWarning("Beacon an {Target} fehlgeschlagen: {Error}", target, ex.Message);
                }
                catch (SocketException)
                {
                }
            }
        } while (await WaitAsync(timer, cancellationToken));
    }

    private static IEnumerable<IPAddress> Targets() =>
        NetworkInfo.LocalIPv4().Select(a => a.Broadcast).OfType<IPAddress>()
            .Append(IPAddress.Broadcast).Distinct();

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try { return await timer.WaitForNextTickAsync(cancellationToken); }
        catch (OperationCanceledException) { return false; }
    }
}
