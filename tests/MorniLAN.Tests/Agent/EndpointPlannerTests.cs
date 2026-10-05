using System.Net;
using MorniLAN.Agent.Connection;
using MorniLAN.Shared.Connection;

namespace MorniLAN.Tests.Agent;

public class EndpointPlannerTests
{
    private static readonly string Pinned = new('A', 64);
    private static readonly string Other = new('B', 64);
    private static readonly AgentState Unpaired = new(Guid.NewGuid());

    private static readonly AgentState Paired = Unpaired with
    {
        Admin = new PinnedAdmin(Pinned, "ADMIN", 47950, ["192.168.1.20", "100.101.102.103", "ADMIN"], "192.168.1.20",
            DateTimeOffset.UnixEpoch),
    };

    private static SeenBeacon Beacon(string fp, string ip, int port = 47950) =>
        new(DiscoveryBeacon.ForAdmin("X", port, fp), IPAddress.Parse(ip), DateTimeOffset.UtcNow);

    [Fact]
    public void Unpaired_WithoutConfigOrBeacon_HasNothingToTry()
    {
        Assert.Empty(EndpointPlanner.Plan(Unpaired, null, 47950, []));
    }

    [Fact]
    public void Unpaired_TriesConfiguredHostFirst_WithoutPin_ThenBeaconsWithTheirFingerprint()
    {
        var plan = EndpointPlanner.Plan(Unpaired, "admin.tail.ts.net", 47950, [Beacon(Other, "192.168.1.50")]);

        Assert.Collection(plan,
            e => Assert.Equal(("admin.tail.ts.net", (string?)null), (e.Host, e.ExpectedFingerprint)),
            e => Assert.Equal(("192.168.1.50", (string?)Other), (e.Host, e.ExpectedFingerprint)));
    }

    [Fact]
    public void Paired_IgnoresForeignAdminPanels()
    {
        var plan = EndpointPlanner.Plan(Paired, null, 47950, [Beacon(Other, "192.168.1.99")]);
        Assert.DoesNotContain(plan, e => e.Host == "192.168.1.99");
        Assert.All(plan, e => Assert.Equal(Pinned, e.ExpectedFingerprint));
    }

    [Fact]
    public void Paired_Order_IsBeacon_Last_Configured_Known_WithoutDuplicates()
    {
        var plan = EndpointPlanner.Plan(Paired, "admin.tail.ts.net", 47950, [Beacon(Pinned, "192.168.1.21", 47960)]);

        Assert.Equal(
            ["192.168.1.21:47960", "192.168.1.20:47950", "admin.tail.ts.net:47950", "100.101.102.103:47950", "ADMIN:47950"],
            plan.Select(e => $"{e.Host}:{e.Port}"));
    }

    [Fact]
    public void Unpaired_BehindRepeater_AlsoTriesAddressesFromBeacon()
    {
        // Repeater im NAT-Modus: Paket kommt von .2, das Panel selbst hat .22.
        var seen = new SeenBeacon(DiscoveryBeacon.ForAdmin("X", 47950, Other, ["192.168.178.22"]),
            IPAddress.Parse("192.168.178.2"), DateTimeOffset.UtcNow);

        var plan = EndpointPlanner.Plan(Unpaired, null, 47950, [seen]);

        Assert.Equal(["192.168.178.2", "192.168.178.22"], plan.Select(e => e.Host));
        Assert.All(plan, e => Assert.Equal(Other, e.ExpectedFingerprint));
    }

    [Fact]
    public void Paired_WithoutLan_StillKnowsTailscaleAddress()
    {
        var plan = EndpointPlanner.Plan(Paired, null, 47950, []);
        Assert.Contains(plan, e => e.Host == "100.101.102.103");
    }
}
