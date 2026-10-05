using System.Text;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Tests.Connection;

public class DiscoveryBeaconTests
{
    private static readonly string Fp = new('A', 64);

    [Fact]
    public void Beacon_RoundTrips()
    {
        var beacon = DiscoveryBeacon.ForAdmin("ADMIN-PC", 47950, Fp);
        Assert.True(DiscoveryBeacon.TryParse(beacon.ToBytes(), out var parsed));
        Assert.Equal(beacon, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hallo")]
    [InlineData("{\"kind\":\"etwas-anderes\",\"version\":1,\"name\":\"X\",\"port\":47950,\"fingerprint\":\"%FP%\"}")]
    [InlineData("{\"kind\":\"mornilan-admin\",\"version\":2,\"name\":\"X\",\"port\":47950,\"fingerprint\":\"%FP%\"}")]
    [InlineData("{\"kind\":\"mornilan-admin\",\"version\":1,\"name\":\"X\",\"port\":0,\"fingerprint\":\"%FP%\"}")]
    [InlineData("{\"kind\":\"mornilan-admin\",\"version\":1,\"name\":\"X\",\"port\":47950,\"fingerprint\":\"zu-kurz\"}")]
    [InlineData("{\"kind\":\"mornilan-admin\",\"version\":1,\"name\":\"\",\"port\":47950,\"fingerprint\":\"%FP%\"}")]
    public void InvalidBeacons_AreIgnored(string json)
    {
        Assert.False(DiscoveryBeacon.TryParse(Encoding.UTF8.GetBytes(json.Replace("%FP%", Fp)), out _));
    }

    [Fact]
    public void OversizedBeacon_IsIgnored()
    {
        Assert.False(DiscoveryBeacon.TryParse(new byte[4096], out _));
    }
}

public class PresenceRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConnectedWithRecentHeartbeat_IsOnline() =>
        Assert.Equal(DevicePresence.Online, PresenceRules.Evaluate(true, Now.AddSeconds(-14), Now));

    [Fact]
    public void ExactlyAtLimit_IsStillOnline() =>
        Assert.Equal(DevicePresence.Online, PresenceRules.Evaluate(true, Now - ConnectionDefaults.OfflineAfter, Now));

    [Fact]
    public void StaleHeartbeat_IsOffline_EvenIfConnected() =>
        Assert.Equal(DevicePresence.Offline, PresenceRules.Evaluate(true, Now.AddSeconds(-46), Now));

    [Fact]
    public void Disconnected_IsOffline() =>
        Assert.Equal(DevicePresence.Offline, PresenceRules.Evaluate(false, Now, Now));

    [Fact]
    public void NeverSeen_IsOffline() =>
        Assert.Equal(DevicePresence.Offline, PresenceRules.Evaluate(true, null, Now));
}
