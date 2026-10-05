using System.Net;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Security;

namespace MorniLAN.Agent.Connection;

/// <param name="ExpectedFingerprint">Zertifikat, das das Panel zeigen muss. null = beliebig (nur vor dem Pairing).</param>
internal sealed record AdminEndpoint(string Host, int Port, string? ExpectedFingerprint, string Source);

/// <summary>Legt fest, welche Adressen der Agent in welcher Reihenfolge probiert.</summary>
internal static class EndpointPlanner
{
    /// <summary>
    /// Gekoppelt: LAN-Beacon des eigenen Panels → zuletzt erfolgreiche Adresse → konfigurierte Adresse →
    /// vom Panel gemeldete Adressen (darunter die Tailscale-IP). Überall muss der Pin passen.
    /// Nicht gekoppelt: konfigurierte Adresse → alle Panels aus dem LAN.
    /// Innerhalb der Beacon- und der bekannten Adressen kommen die aus dem eigenen Netz zuerst: Jede
    /// unerreichbare Adresse kostet bis zu 8 Sekunden (Repeater, VPN-Adapter, Hyper-V).
    /// </summary>
    public static IReadOnlyList<AdminEndpoint> Plan(AgentState state, string? configuredHost, int configuredPort,
        IReadOnlyList<SeenBeacon> beacons, IReadOnlyList<NetworkInfo.LocalAddress>? localNetworks = null)
    {
        var local = localNetworks ?? [];
        var plan = new List<AdminEndpoint>();
        var configured = string.IsNullOrWhiteSpace(configuredHost) ? null : configuredHost.Trim();

        if (state.Admin is { } admin)
        {
            foreach (var seen in beacons.Where(b => CertificateFingerprint.AreEqual(b.Beacon.Fingerprint, admin.Fingerprint)))
                plan.AddRange(FromBeacon(seen, admin.Fingerprint, local));
            if (admin.LastEndpoint is { } last)
                plan.Add(new AdminEndpoint(last, admin.Port, admin.Fingerprint, "zuletzt"));
            if (configured is not null)
                plan.Add(new AdminEndpoint(configured, configuredPort, admin.Fingerprint, "Einstellung"));
            foreach (var endpoint in OwnNetworkFirst(admin.Endpoints, e => IPAddress.TryParse(e, out var ip) ? ip : null, local))
                plan.Add(new AdminEndpoint(endpoint, admin.Port, admin.Fingerprint, "bekannt"));
        }
        else
        {
            if (configured is not null)
                plan.Add(new AdminEndpoint(configured, configuredPort, null, "Einstellung"));
            foreach (var seen in beacons)
                plan.AddRange(FromBeacon(seen, seen.Beacon.Fingerprint, local));
        }

        return [.. plan.DistinctBy(e => (e.Host.ToLowerInvariant(), e.Port))];
    }

    /// <summary>
    /// Absenderadresse und die Adressen, die das Panel selbst meldet. Steht die Absenderadresse nicht in
    /// dieser Liste, hat ein Repeater sie umgeschrieben (beim Nutzer 192.168.178.2): dann kommt sie zuletzt.
    /// </summary>
    private static IEnumerable<AdminEndpoint> FromBeacon(SeenBeacon seen, string fingerprint,
        IReadOnlyList<NetworkInfo.LocalAddress> local)
    {
        var reported = seen.Beacon.ValidAddresses();
        var rewritten = reported.Count > 0 && !reported.Contains(seen.Address);
        var candidates = rewritten ? reported.Append(seen.Address) : reported.Prepend(seen.Address);
        return OwnNetworkFirst(candidates.Distinct(), ip => ip, local)
            .Select(ip => new AdminEndpoint(ip.ToString(), seen.Beacon.Port, fingerprint, "LAN"));
    }

    /// <summary>Stabile Sortierung: zuerst, was im Netz einer eigenen (nicht virtuellen) Adresse liegt.</summary>
    private static IEnumerable<T> OwnNetworkFirst<T>(IEnumerable<T> items, Func<T, IPAddress?> address,
        IReadOnlyList<NetworkInfo.LocalAddress> local) =>
        items.OrderBy(item => address(item) is { } ip && local.Any(l => !l.IsVirtual && l.Contains(ip)) ? 0 : 1);
}
