using MorniLAN.Shared.Models;

namespace MorniLAN.Tests.Shared;

public class ApprovalSetTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnknownApp_IsBlockedByDefault()
    {
        Assert.False(new ApprovalSet().IsApproved("steam:730"));
    }

    [Fact]
    public void ApprovedApp_IsAllowed_AndCanBeRevoked()
    {
        var set = new ApprovalSet();
        set.Apply(new ApprovalRule("steam:730", true, T0));
        Assert.True(set.IsApproved("steam:730"));

        set.Apply(new ApprovalRule("steam:730", false, T0.AddMinutes(1)));
        Assert.False(set.IsApproved("steam:730"));
    }

    [Fact]
    public void OlderRule_DoesNotOverwriteNewer()
    {
        var set = new ApprovalSet();
        set.Apply(new ApprovalRule("steam:730", false, T0.AddMinutes(5)));

        var changed = set.Apply(new ApprovalRule("steam:730", true, T0));

        Assert.False(changed);
        Assert.False(set.IsApproved("steam:730"));
    }

    [Fact]
    public void ReapplyingSameRule_ReportsNoChange()
    {
        var rule = new ApprovalRule("exe:0011223344556677", true, T0);
        var set = new ApprovalSet([rule]);
        Assert.False(set.Apply(rule));
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        var set = new ApprovalSet([new ApprovalRule("EXE:ABCDEF", true, T0)]);
        Assert.True(set.IsApproved("exe:abcdef"));
    }

    [Fact]
    public void FilterApproved_ReturnsOnlyApprovedEntries()
    {
        var cs = new AppEntry("steam:730", "Counter-Strike 2", AppSource.Steam, SteamAppId: 730);
        var dota = new AppEntry("steam:570", "Dota 2", AppSource.Steam, SteamAppId: 570);
        var set = new ApprovalSet([
            new ApprovalRule(cs.Id, true, T0),
            new ApprovalRule(dota.Id, false, T0),
        ]);

        Assert.Equal(new[] { cs }, set.FilterApproved([cs, dota]).ToArray());
    }
}
