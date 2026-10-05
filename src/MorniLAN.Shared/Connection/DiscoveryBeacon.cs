using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MorniLAN.Shared.Security;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Shared.Connection;

/// <summary>
/// UDP-Broadcast, mit dem sich das Admin-Panel im LAN bekannt macht.
/// Enthält keine Geheimnisse, nur wie man das Panel erreicht und welches Zertifikat es hat.
/// </summary>
/// <param name="Addresses">
/// Eigene LAN-Adressen des Panels. Nötig, weil die Absenderadresse des Pakets nicht stimmen muss:
/// Ein WLAN-Repeater im NAT-Modus schreibt sie auf seine eigene um (beim Nutzer: 192.168.178.2
/// statt 192.168.178.22). Fehlt bei älteren Panels.
/// </param>
public sealed record DiscoveryBeacon(string Kind, int Version, string Name, int Port, string Fingerprint,
    string[]? Addresses = null)
{
    public const string AdminKind = "mornilan-admin";
    public const int CurrentVersion = 1;
    private const int MaxSize = 1024;
    private const int MaxAddresses = 16;

    public static DiscoveryBeacon ForAdmin(string name, int port, string fingerprint, string[]? addresses = null) =>
        new(AdminKind, CurrentVersion, name, port, fingerprint, addresses);

    /// <summary>Gültige IPv4-Adressen aus <see cref="Addresses"/>.</summary>
    public IReadOnlyList<System.Net.IPAddress> ValidAddresses() =>
        [.. (Addresses ?? []).Take(MaxAddresses)
            .Select(a => System.Net.IPAddress.TryParse(a, out var ip) ? ip : null)
            .OfType<System.Net.IPAddress>()
            .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)];

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, MorniLanJsonContext.Default.DiscoveryBeacon);

    /// <summary>
    /// Suchanfrage eines Agents („Ist hier ein Admin-Panel?“). Das Panel antwortet direkt an den
    /// Absender mit seinem Beacon. Das klappt auch dort, wo der Router Broadcasts vom Kabel-LAN
    /// nicht ins WLAN weiterreicht.
    /// </summary>
    public static ReadOnlySpan<byte> Query => "MORNILAN-QUERY-1"u8;

    public static bool IsQuery(ReadOnlySpan<byte> data) => data.SequenceEqual(Query);

    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out DiscoveryBeacon? beacon)
    {
        beacon = null;
        if (data.IsEmpty || data.Length > MaxSize)
            return false;
        try
        {
            var parsed = JsonSerializer.Deserialize(data, MorniLanJsonContext.Default.DiscoveryBeacon);
            if (parsed is not { Kind: AdminKind, Version: CurrentVersion } || parsed.Port is < 1 or > 65535
                || !CertificateFingerprint.IsValid(parsed.Fingerprint) || string.IsNullOrWhiteSpace(parsed.Name))
                return false;
            beacon = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
