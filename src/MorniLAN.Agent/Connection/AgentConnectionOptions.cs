using MorniLAN.Shared;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Agent.Connection;

/// <summary>
/// Einstellungen aus appsettings.json, Abschnitt "MorniLAN:Connection".
/// Für Tailscale ohne gemeinsames LAN: AdminHost auf die Tailscale-IP oder den MagicDNS-Namen setzen.
/// </summary>
public sealed class AgentConnectionOptions
{
    public const string Section = "MorniLAN:Connection";

    public string DataDirectory { get; set; } = AgentPaths.Data;

    /// <summary>Optional: feste Adresse des Admin-Panels (z. B. "admin-pc.tail1234.ts.net").</summary>
    public string? AdminHost { get; set; }

    public int AdminPort { get; set; } = MorniLanConstants.AdminPort;

    public bool EnableDiscovery { get; set; } = true;

    public int DiscoveryPort { get; set; } = MorniLanConstants.DiscoveryPort;

    public TimeSpan HeartbeatInterval { get; set; } = ConnectionDefaults.HeartbeatInterval;

    /// <summary>So oft fragt der Agent im LAN nach einem Admin-Panel.</summary>
    public TimeSpan DiscoveryQueryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Nur für Tests: feste Ziele für Suchanfragen statt der Broadcast-Adressen.</summary>
    internal IReadOnlyList<System.Net.IPEndPoint>? DiscoveryQueryTargets { get; set; }
}
