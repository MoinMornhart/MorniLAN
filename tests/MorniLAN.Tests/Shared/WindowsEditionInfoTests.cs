using MorniLAN.Shared.Models;

namespace MorniLAN.Tests.Shared;

public class WindowsEditionInfoTests
{
    [Theory]
    [InlineData("Core", EditionFamily.Home)]
    [InlineData("CoreSingleLanguage", EditionFamily.Home)]
    [InlineData("Professional", EditionFamily.Pro)]
    [InlineData("ProfessionalWorkstation", EditionFamily.Pro)]
    [InlineData("ProfessionalEducation", EditionFamily.Education)]
    [InlineData("Education", EditionFamily.Education)]
    [InlineData("Enterprise", EditionFamily.Enterprise)]
    [InlineData("EnterpriseS", EditionFamily.Enterprise)]
    [InlineData("IoTEnterprise", EditionFamily.Enterprise)]
    [InlineData("ServerStandard", EditionFamily.Server)]
    [InlineData("", EditionFamily.Unknown)]
    [InlineData(null, EditionFamily.Unknown)]
    [InlineData("Banana", EditionFamily.Unknown)]
    public void ClassifiesEditionIds(string? editionId, EditionFamily expected)
    {
        Assert.Equal(expected, WindowsEditionInfo.ClassifyEdition(editionId));
    }

    [Fact]
    public void Windows11_IsReportedCorrectlyDespiteRegistryProductName()
    {
        var info = new WindowsEditionInfo("Professional", "Windows 10 Pro", "25H2", 26200);
        Assert.Equal("Windows 11 Pro 25H2", info.FriendlyName);
    }

    [Fact]
    public void Windows10_StaysWindows10()
    {
        var info = new WindowsEditionInfo("Core", "Windows 10 Home", "22H2", 19045);
        Assert.Equal("Windows 10 Home 22H2", info.FriendlyName);
    }

    [Theory]
    [InlineData("Core", 26200, LockdownMechanism.AppControl, false)]
    [InlineData("Professional", 26200, LockdownMechanism.AppControl, true)]
    [InlineData("Enterprise", 26200, LockdownMechanism.AppLocker, true)]
    [InlineData("Core", 17763, LockdownMechanism.ProcessWatcher, false)]
    [InlineData("Banana", 26200, LockdownMechanism.ProcessWatcher, false)]
    public void ChoosesLockdownAndRdpByEdition(string editionId, int build, LockdownMechanism lockdown, bool rdp)
    {
        var info = new WindowsEditionInfo(editionId, "Windows", "", build);
        Assert.Equal(lockdown, info.PreferredLockdown);
        Assert.Equal(rdp, info.SupportsRdpHost);
    }
}
