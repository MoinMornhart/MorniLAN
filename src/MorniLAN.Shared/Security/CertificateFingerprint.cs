using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MorniLAN.Shared.Security;

/// <summary>SHA-256 über das ganze Zertifikat, 64 Hex-Zeichen in Großbuchstaben. Basis fürs Pinning.</summary>
public static class CertificateFingerprint
{
    public static string Of(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

    public static bool IsValid(string? fingerprint) =>
        fingerprint is { Length: 64 } && fingerprint.All(Uri.IsHexDigit);

    public static bool AreEqual(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Kurzform für die Anzeige, z. B. "3FA2 91C0 7B4E 22D1".</summary>
    public static string Short(string fingerprint) =>
        fingerprint.Length < 16
            ? fingerprint
            : string.Join(' ', Enumerable.Range(0, 4).Select(i => fingerprint.Substring(i * 4, 4)));
}
