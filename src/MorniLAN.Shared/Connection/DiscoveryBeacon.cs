using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MorniLAN.Shared.Security;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Shared.Connection;

/// <summary>
/// UDP-Broadcast, mit dem sich das Admin-Panel im LAN bekannt macht.
/// Enthält keine Geheimnisse, nur wie man das Panel erreicht und welches Zertifikat es hat.
/// </summary>
public sealed record DiscoveryBeacon(string Kind, int Version, string Name, int Port, string Fingerprint)
{
    public const string AdminKind = "mornilan-admin";
    public const int CurrentVersion = 1;
    private const int MaxSize = 1024;

    public static DiscoveryBeacon ForAdmin(string name, int port, string fingerprint) =>
        new(AdminKind, CurrentVersion, name, port, fingerprint);

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
