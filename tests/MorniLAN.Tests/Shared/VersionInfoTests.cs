using System.Text.RegularExpressions;
using MorniLAN.Shared;

namespace MorniLAN.Tests.Shared;

public class VersionInfoTests
{
    [Fact]
    public void Version_IsSemVer()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$"), VersionInfo.Version);
    }

    [Fact]
    public void Display_StartsWithV()
    {
        Assert.StartsWith("v" + VersionInfo.Version, VersionInfo.Display);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0", null)]
    [InlineData("0.1.0+a1b2c3d4e5f6", "0.1.0", "a1b2c3d")]
    [InlineData("1.2.3-beta.1+abc", "1.2.3-beta.1", "abc")]
    [InlineData("1.0.0+", "1.0.0", null)]
    public void ParsesInformationalVersion(string informational, string version, string? commit)
    {
        Assert.Equal(version, VersionInfo.StripMetadata(informational));
        Assert.Equal(commit, VersionInfo.ExtractCommit(informational));
    }
}
