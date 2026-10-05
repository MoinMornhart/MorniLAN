using MorniLAN.Shared.Connection;

namespace MorniLAN.Tests.Connection;

public class PairingProofTests
{
    private const string Code = "K7Q2M9XD";
    private static readonly string AgentFp = new('A', 64);
    private static readonly string AdminFp = new('B', 64);
    private static readonly PairingTranscript Transcript = new(Guid.Parse("6f1c0c5e-4c1e-4a7a-9d55-1f0e3b0f2a11"), AgentFp, AdminFp);

    private static string Prove(string code, PairingRole role, PairingTranscript transcript) =>
        Convert.ToBase64String(PairingProof.Sign(PairingProof.DeriveKey(code, transcript), role, transcript));

    [Fact]
    public void SameCodeAndTranscript_Verifies()
    {
        var key = PairingProof.DeriveKey(Code, Transcript);
        Assert.True(PairingProof.Verify(key, PairingRole.Agent, Transcript, Prove(Code, PairingRole.Agent, Transcript)));
        Assert.True(PairingProof.Verify(key, PairingRole.Admin, Transcript, Prove(Code, PairingRole.Admin, Transcript)));
    }

    [Fact]
    public void WrongCode_DoesNotVerify()
    {
        var key = PairingProof.DeriveKey("K7Q2M9XE", Transcript);
        Assert.False(PairingProof.Verify(key, PairingRole.Agent, Transcript, Prove(Code, PairingRole.Agent, Transcript)));
    }

    [Fact]
    public void ManInTheMiddleCertificate_DoesNotVerify()
    {
        // Agent hat das Zertifikat eines Angreifers gesehen, das Panel sein eigenes.
        var seenByAgent = Transcript with { AdminFingerprint = new string('C', 64) };
        var agentProof = Prove(Code, PairingRole.Agent, seenByAgent);

        var adminKey = PairingProof.DeriveKey(Code, Transcript);
        Assert.False(PairingProof.Verify(adminKey, PairingRole.Agent, Transcript, agentProof));
    }

    [Fact]
    public void AgentProof_CannotBeReplayedAsAdminProof()
    {
        var key = PairingProof.DeriveKey(Code, Transcript);
        var agentProof = Prove(Code, PairingRole.Agent, Transcript);
        Assert.False(PairingProof.Verify(key, PairingRole.Admin, Transcript, agentProof));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein base64!")]
    [InlineData("AAAA")]
    public void GarbageProof_DoesNotVerify(string? proof)
    {
        var key = PairingProof.DeriveKey(Code, Transcript);
        Assert.False(PairingProof.Verify(key, PairingRole.Agent, Transcript, proof));
    }
}
