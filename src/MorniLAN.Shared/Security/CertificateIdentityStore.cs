using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MorniLAN.Shared.Security;

/// <summary>
/// Eigenes selbstsigniertes Zertifikat (Agent oder Admin-Panel), gespeichert als PFX,
/// das per DPAPI an das Windows-Konto gebunden ist (beim Dienst: LocalSystem).
/// Ein anderes Konto kann die Datei nicht entschlüsseln.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CertificateIdentityStore
{
    private static readonly byte[] Entropy = "MorniLAN-identity-v1"u8.ToArray();

    /// <summary>Lädt das Zertifikat oder legt ein neues an.</summary>
    /// <param name="created">true, wenn neu erzeugt wurde (dann ist ein neues Pairing nötig).</param>
    public static X509Certificate2 LoadOrCreate(string path, string subjectName, out bool created)
    {
        if (File.Exists(path))
        {
            try
            {
                var pfx = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                created = false;
                return Load(pfx);
            }
            catch (CryptographicException)
            {
                // Datei gehört einem anderen Konto oder ist beschädigt → neu anlegen.
            }
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var fresh = Create(subjectName);
        var bytes = fresh.Export(X509ContentType.Pfx);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
        File.Move(temp, path, overwrite: true);
        created = true;
        return Load(bytes);
    }

    internal static X509Certificate2 Create(string subjectName)
    {
        // RSA statt ECDSA: am verträglichsten mit SChannel bei Client-Zertifikaten.
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subjectName}, O=MorniLAN", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], false)); // Server + Client
        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddDays(-1), now.AddYears(20));
    }

    // Nicht als EphemeralKeySet laden: SChannel (TLS unter Windows) braucht einen gespeicherten Schlüssel.
    private static X509Certificate2 Load(byte[] pfx) =>
        X509CertificateLoader.LoadPkcs12(pfx, password: null, X509KeyStorageFlags.DefaultKeySet);
}
