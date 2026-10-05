using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MorniLAN.Admin.Server;

/// <summary>Eigene IPv4-Adressen und Broadcast-Ziele des Admin-PCs.</summary>
internal static class NetworkInfo
{
    internal readonly record struct LocalAddress(IPAddress Address, IPAddress? Broadcast, bool IsTailscale);

    public static IReadOnlyList<LocalAddress> LocalIPv4()
    {
        var result = new List<LocalAddress>();
        NetworkInterface[] interfaces;
        try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return result; }

        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IsLinkLocal(ip))
                    continue;
                var tailscale = IsTailscale(ip);
                result.Add(new LocalAddress(ip, tailscale ? null : BroadcastOf(ip, unicast.PrefixLength), tailscale));
            }
        }
        return result;
    }

    /// <summary>
    /// Adressen für den Agent, in dieser Reihenfolge: LAN, Tailscale, Rechnername.
    /// Über die Tailscale-IP findet der Agent das Panel auch, wenn er nicht mehr im selben LAN steht.
    /// </summary>
    public static string[] AdminEndpoints()
    {
        var addresses = LocalIPv4();
        return
        [
            .. addresses.Where(a => !a.IsTailscale).Select(a => a.Address.ToString()),
            .. addresses.Where(a => a.IsTailscale).Select(a => a.Address.ToString()),
            Environment.MachineName,
        ];
    }

    /// <summary>Tailscale vergibt Adressen aus 100.64.0.0/10 (CGNAT-Bereich).</summary>
    public static bool IsTailscale(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && b[0] == 100 && (b[1] & 0xC0) == 64;
    }

    internal static IPAddress? BroadcastOf(IPAddress ip, int prefixLength)
    {
        if (prefixLength is <= 0 or >= 31)
            return null;
        var address = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(ip.GetAddressBytes()));
        var mask = uint.MaxValue << (32 - prefixLength);
        var broadcast = address | ~mask;
        return new IPAddress(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)broadcast)));
    }

    public static string? Display(IPAddress? ip) =>
        ip is null ? null : ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();

    private static bool IsLinkLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }
}
