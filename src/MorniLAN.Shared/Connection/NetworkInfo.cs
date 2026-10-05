using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MorniLAN.Shared.Connection;

/// <summary>Eigene IPv4-Adressen und Broadcast-Ziele des Admin-PCs.</summary>
public static class NetworkInfo
{
    /// <param name="IsVirtual">Adapter von Hyper-V, VirtualBox, VMware oder einem VPN: für andere PCs meist unerreichbar.</param>
    public readonly record struct LocalAddress(IPAddress Address, IPAddress? Broadcast, bool IsTailscale, bool IsVirtual = false)
    {
        public string Kind => IsTailscale ? "Tailscale" : IsVirtual ? "Virtuell" : "Heimnetz";
    }

    private static readonly string[] VirtualMarkers =
        ["Hyper-V", "VirtualBox", "VMware", "Virtual Ethernet", "vEthernet", "ZeroTier", "Radmin", "Hamachi", "TAP-Windows",
         "WireGuard", "OpenVPN", "Npcap"];

    internal static bool LooksVirtual(string name, string description) =>
        VirtualMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)
                                || description.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>Eigene IPv4-Adressen, sortiert: Heimnetz, Tailscale, virtuelle Adapter.</summary>
    public static IReadOnlyList<LocalAddress> LocalIPv4() =>
        [.. Collect().OrderBy(a => a.IsTailscale ? 1 : a.IsVirtual ? 2 : 0)];

    private static List<LocalAddress> Collect()
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
                result.Add(new LocalAddress(ip, tailscale ? null : BroadcastOf(ip, unicast.PrefixLength), tailscale,
                    !tailscale && LooksVirtual(nic.Name, nic.Description)));
            }
        }
        return result;
    }

    /// <summary>
    /// Adressen für den Agent, in dieser Reihenfolge: Heimnetz, Tailscale, virtuelle Adapter, Rechnername.
    /// Über die Tailscale-IP findet der Agent das Panel auch, wenn er nicht mehr im selben LAN steht.
    /// </summary>
    public static string[] AdminEndpoints() =>
        [.. LocalIPv4().Select(a => a.Address.ToString()), Environment.MachineName];

    /// <summary>Tailscale vergibt Adressen aus 100.64.0.0/10 (CGNAT-Bereich).</summary>
    public static bool IsTailscale(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && b[0] == 100 && (b[1] & 0xC0) == 64;
    }

    public static IPAddress? BroadcastOf(IPAddress ip, int prefixLength)
    {
        if (prefixLength is <= 0 or >= 31)
            return null;
        var address = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(ip.GetAddressBytes()));
        var mask = uint.MaxValue << (32 - prefixLength);
        var broadcast = address | ~mask;
        return new IPAddress(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)broadcast)));
    }

    /// <summary>Broadcast-Adressen aller LAN-Netze plus 255.255.255.255.</summary>
    public static IReadOnlyList<IPAddress> BroadcastTargets() =>
        [.. LocalIPv4().Select(a => a.Broadcast).OfType<IPAddress>().Append(IPAddress.Broadcast).Distinct()];

    /// <summary>
    /// UDP-Socket für die LAN-Suche: an den Port gebunden (mit ReuseAddress, damit Agent und Panel
    /// auf einem Rechner laufen können), Broadcast erlaubt. Ohne SIO_UDP_CONNRESET bricht Windows
    /// das Empfangen ab, sobald ein Ziel mit "Port nicht erreichbar" antwortet.
    /// </summary>
    public static UdpClient OpenDiscoverySocket(int port)
    {
        var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        try
        {
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            if (OperatingSystem.IsWindows())
            {
                const int SioUdpConnReset = -1744830452;
                udp.Client.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
            }
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            return udp;
        }
        catch
        {
            udp.Dispose();
            throw;
        }
    }

    public static string? Display(IPAddress? ip) =>
        ip is null ? null : ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();

    private static bool IsLinkLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }
}
