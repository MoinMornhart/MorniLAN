using System.Security.Cryptography;
using System.Text;

namespace MorniLAN.Shared.Connection;

public enum PairingRole
{
    Agent,
    Admin,
}

/// <summary>
/// Was beide Seiten beim Pairing gesehen haben. Weil beide Zertifikat-Fingerabdrücke einfließen,
/// passt der Beweis nicht mehr, wenn sich jemand dazwischenschaltet (anderes Zertifikat).
/// </summary>
public sealed record PairingTranscript(Guid DeviceId, string AgentFingerprint, string AdminFingerprint)
{
    internal byte[] ToBytes() =>
        Encoding.UTF8.GetBytes($"MorniLAN-Pairing-v1|{DeviceId:N}|{AgentFingerprint}|{AdminFingerprint}");
}

/// <summary>
/// Gegenseitiger Beweis, dass beide Seiten denselben Pairing-Code kennen, ohne ihn zu übertragen.
/// Schlüssel = PBKDF2-SHA256(Code, Transkript), Beweis = HMAC-SHA256(Schlüssel, Rolle + Transkript).
/// Die vielen PBKDF2-Runden machen Durchprobieren aller Codes aus einem mitgeschnittenen Beweis teuer.
/// </summary>
public static class PairingProof
{
    public const int Iterations = 200_000;

    public static byte[] DeriveKey(string normalizedCode, PairingTranscript transcript) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(normalizedCode), transcript.ToBytes(), Iterations,
            HashAlgorithmName.SHA256, 32);

    public static byte[] Sign(byte[] key, PairingRole role, PairingTranscript transcript)
    {
        var label = Encoding.UTF8.GetBytes(role == PairingRole.Agent ? "agent|" : "admin|");
        byte[] message = [.. label, .. transcript.ToBytes()];
        return HMACSHA256.HashData(key, message);
    }

    public static bool Verify(byte[] key, PairingRole role, PairingTranscript transcript, string? proofBase64)
    {
        if (string.IsNullOrEmpty(proofBase64))
            return false;
        byte[] proof;
        try { proof = Convert.FromBase64String(proofBase64); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(proof, Sign(key, role, transcript));
    }
}
