using MorniLAN.Shared.Security;

namespace MorniLAN.Tests.Connection;

public class CertificateTests
{
    [Fact]
    public void Fingerprint_IsSha256Hex()
    {
        using var cert = CertificateIdentityStore.Create("Test");
        var fp = CertificateFingerprint.Of(cert);
        Assert.True(CertificateFingerprint.IsValid(fp));
        Assert.True(CertificateFingerprint.AreEqual(fp, fp.ToLowerInvariant()));
        Assert.Equal(19, CertificateFingerprint.Short(fp).Length); // "XXXX XXXX XXXX XXXX"
    }

    [Fact]
    public void IdentityStore_CreatesOnce_ThenLoadsSameCertificateWithKey()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "identity.bin");

        using var first = CertificateIdentityStore.LoadOrCreate(path, "Test", out var created);
        Assert.True(created);
        Assert.True(first.HasPrivateKey);

        using var second = CertificateIdentityStore.LoadOrCreate(path, "Test", out created);
        Assert.False(created);
        Assert.True(second.HasPrivateKey);
        Assert.Equal(CertificateFingerprint.Of(first), CertificateFingerprint.Of(second));
    }

    [Fact]
    public void IdentityStore_FileIsNotAPlainPfx()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "identity.bin");
        using var cert = CertificateIdentityStore.LoadOrCreate(path, "Test", out _);

        // DPAPI-Blob, kein PFX: ohne das Windows-Konto nicht ladbar.
        Assert.ThrowsAny<Exception>(() =>
            System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(path, null));
    }

    [Fact]
    public void IdentityStore_ReplacesCorruptFile()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "identity.bin");
        File.WriteAllBytes(path, [1, 2, 3]);

        using var cert = CertificateIdentityStore.LoadOrCreate(path, "Test", out var created);
        Assert.True(created);
        Assert.True(cert.HasPrivateKey);
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mornilan-tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
