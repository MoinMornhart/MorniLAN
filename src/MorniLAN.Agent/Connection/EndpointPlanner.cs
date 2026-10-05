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
    /// </summary>
    public static IReadOnlyList<AdminEndpoint> Plan(AgentState state, string? configuredHost, int configuredPort,
        IReadOnlyList<SeenBeacon> beacons)
    {
        var plan = new List<AdminEndpoint>();
        var configured = string.IsNullOrWhiteSpace(configuredHost) ? null : configuredHost.Trim();

        if (state.Admin is { } admin)
        {
            foreach (var seen in beacons.Where(b => CertificateFingerprint.AreEqual(b.Beacon.Fingerprint, admin.Fingerprint)))
                plan.Add(new AdminEndpoint(seen.Address.ToString(), seen.Beacon.Port, admin.Fingerprint, "LAN"));
            if (admin.LastEndpoint is { } last)
                plan.Add(new AdminEndpoint(last, admin.Port, admin.Fingerprint, "zuletzt"));
            if (configured is not null)
                plan.Add(new AdminEndpoint(configured, configuredPort, admin.Fingerprint, "Einstellung"));
            foreach (var endpoint in admin.Endpoints)
                plan.Add(new AdminEndpoint(endpoint, admin.Port, admin.Fingerprint, "bekannt"));
        }
        else
        {
            if (configured is not null)
                plan.Add(new AdminEndpoint(configured, configuredPort, null, "Einstellung"));
            foreach (var seen in beacons)
                plan.Add(new AdminEndpoint(seen.Address.ToString(), seen.Beacon.Port, seen.Beacon.Fingerprint, "LAN"));
        }

        return [.. plan.DistinctBy(e => (e.Host.ToLowerInvariant(), e.Port))];
    }
}
