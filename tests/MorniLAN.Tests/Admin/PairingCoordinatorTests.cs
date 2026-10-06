using MorniLAN.Admin.Server;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Security;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Admin;

public sealed class PairingCoordinatorTests : IDisposable
{
    private const string Code = "K7Q2M9XD";
    private const string Connection = "conn-1";
    private static readonly string AgentFp = new('A', 64);

    private readonly TempDirectory _dir = new();
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _adminCert =
        CertificateIdentityStore.Create("Admin");
    private readonly DeviceRegistry _registry;
    private readonly AdminIdentity _identity;
    private readonly PairingCoordinator _pairing;
    private readonly FakeAgentClient _client = new();
    private readonly DeviceInfo _device = new(Guid.NewGuid(), "FREUND-PC",
        new WindowsEditionInfo("Core", "Windows 10 Home", "24H2", 26100), "0.1.0");

    public PairingCoordinatorTests()
    {
        _registry = new DeviceRegistry(_dir.Path);
        _identity = new AdminIdentity(_adminCert, "ADMIN");
        _pairing = new PairingCoordinator(_registry, _identity, TimeProvider.System) { ClientResolver = _ => _client };
    }

    public void Dispose()
    {
        _adminCert.Dispose();
        _dir.Dispose();
    }

    private PairingTranscript Transcript => new(_device.DeviceId, AgentFp, _identity.Fingerprint);

    private Guid Register()
    {
        var key = PairingProof.DeriveKey(Code, Transcript);
        var proof = Convert.ToBase64String(PairingProof.Sign(key, PairingRole.Agent, Transcript));
        _pairing.Register(Connection, AgentFp, new PairingRequest(_device, proof), "192.168.1.50");
        return Assert.Single(_pairing.Snapshot()).RequestId;
    }

    [Fact]
    public async Task CorrectCode_SendsValidAdminProof_AndDeviceIsStoredOnlyAfterConfirm()
    {
        var id = Register();

        Assert.Equal(PairingSubmitResult.Approved, await _pairing.SubmitCodeAsync(id, "k7q2-m9xd"));
        var approval = Assert.IsType<PairingApproval>(_client.Approval);
        var key = PairingProof.DeriveKey(Code, Transcript);
        Assert.True(PairingProof.Verify(key, PairingRole.Admin, Transcript, approval.AdminProof));
        Assert.Empty(_pairing.Snapshot());
        Assert.Empty(_registry.Snapshot());

        var device = _pairing.Complete(Connection, AgentFp);
        Assert.NotNull(device);
        Assert.True(_registry.IsPaired(_device.DeviceId, AgentFp));
        Assert.True(new DeviceRegistry(_dir.Path).IsPaired(_device.DeviceId, AgentFp)); // gespeichert
    }

    [Fact]
    public async Task Complete_FailsWithoutApproval_OrWithOtherCertificate()
    {
        var id = Register();
        Assert.Null(_pairing.Complete(Connection, AgentFp));

        await _pairing.SubmitCodeAsync(id, Code);
        Assert.Null(_pairing.Complete(Connection, new string('C', 64)));
        Assert.Null(_pairing.Complete("andere-verbindung", AgentFp));
        Assert.Empty(_registry.Snapshot());
    }

    [Fact]
    public async Task WrongCode_CountsAttempts_AndRejectsAfterLimit()
    {
        var id = Register();

        for (var i = 1; i < ConnectionDefaults.MaxPairingAttempts; i++)
        {
            Assert.Equal(PairingSubmitResult.WrongCode, await _pairing.SubmitCodeAsync(id, "AAAA-AAAA"));
            Assert.Equal(i, Assert.Single(_pairing.Snapshot()).FailedAttempts);
        }

        Assert.Equal(PairingSubmitResult.TooManyAttempts, await _pairing.SubmitCodeAsync(id, "AAAA-AAAA"));
        Assert.Empty(_pairing.Snapshot());
        Assert.NotNull(_client.Rejection);
        Assert.Null(_client.Approval);
    }

    [Fact]
    public async Task InvalidFormat_DoesNotCountAsAttempt()
    {
        var id = Register();
        Assert.Equal(PairingSubmitResult.InvalidFormat, await _pairing.SubmitCodeAsync(id, "123"));
        Assert.Equal(0, Assert.Single(_pairing.Snapshot()).FailedAttempts);
    }

    [Fact]
    public async Task Reject_RemovesRequest_AndNotifiesAgent()
    {
        var id = Register();
        await _pairing.RejectAsync(id);
        Assert.Empty(_pairing.Snapshot());
        Assert.NotNull(_client.Rejection);
        Assert.Equal(PairingSubmitResult.NotFound, await _pairing.SubmitCodeAsync(id, Code));
    }

    [Fact]
    public void ClosedConnection_DropsRequest()
    {
        Register();
        _pairing.ConnectionClosed(Connection);
        Assert.Empty(_pairing.Snapshot());
    }

    private sealed class FakeAgentClient : IAgentClient
    {
        public PairingApproval? Approval { get; private set; }
        public string? Rejection { get; private set; }

        public Task OnPairingApproved(PairingApproval approval)
        {
            Approval = approval;
            return Task.CompletedTask;
        }

        public Task OnPairingRejected(string reason)
        {
            Rejection = reason;
            return Task.CompletedTask;
        }

        public Task OnUnpaired() => Task.CompletedTask;

        public Task OnRefreshInventory() => Task.CompletedTask;

        public Task OnInstallUpdate() => Task.CompletedTask;

        public Task OnPolicyChanged(MorniLAN.Shared.Models.AppPolicy policy) => Task.CompletedTask;

        public Task OnCreateProfile(MorniLAN.Shared.Models.LauncherProfile profile) => Task.CompletedTask;

        public Task OnDeleteProfile(string profileId) => Task.CompletedTask;

        public Task OnApplyRestrictions() => Task.CompletedTask;

        public Task OnResetProfilePassword(string profileId) => Task.CompletedTask;
    }
}
