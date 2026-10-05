using System.Text.Json;
using MorniLAN.Shared.Updates;

namespace MorniLAN.Tests.Shared;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("0.3.0", "0.2.0")]
    [InlineData("0.3.0", "0.3.0-beta.9")]
    [InlineData("0.3.0-beta.10", "0.3.0-beta.2")] // Zahlen als Zahlen, nicht als Text
    [InlineData("0.3.0-beta.2", "0.3.0-beta.1")]
    [InlineData("0.3.0-beta.1", "0.3.0-alpha.5")]
    [InlineData("0.3.0-beta.1.1", "0.3.0-beta.1")]
    [InlineData("1.0.0", "0.99.99")]
    [InlineData("0.10.0", "0.9.0")]
    public void Ordering_FollowsSemVer(string higher, string lower)
    {
        Assert.True(SemanticVersion.Parse(higher) > SemanticVersion.Parse(lower));
        Assert.True(SemanticVersion.Parse(lower) < SemanticVersion.Parse(higher));
    }

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.2.0+4b42c20", "0.2.0")]
    [InlineData("0.2.0-beta.3+8340ad3", "0.2.0-beta.3")]
    public void Parse_AcceptsTagsAndBuildMetadata(string text, string expected)
    {
        Assert.Equal(expected, SemanticVersion.Parse(text).ToString());
        Assert.Equal(SemanticVersion.Parse(expected), SemanticVersion.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0.2")]
    [InlineData("0.2.x")]
    [InlineData("0.2.0-")]
    [InlineData("0.2.0-beta..1")]
    public void Parse_RejectsGarbage(string text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }
}

public class GitHubReleasesTests
{
    // Ausschnitt im Format der GitHub-API (gekürzt)
    private const string ApiResponse = """
        [
          { "tag_name": "v0.3.0-beta.1", "draft": false, "prerelease": true, "html_url": "https://github.com/x/releases/tag/v0.3.0-beta.1", "body": "Neu",
            "assets": [
              { "name": "MorniLAN-Admin-Setup-0.3.0-beta.1.exe", "browser_download_url": "https://github.com/x/a.exe", "size": 100, "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              { "name": "MorniLAN-Geraete-Setup-0.3.0-beta.1.exe", "browser_download_url": "https://github.com/x/g.exe", "size": 200, "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
            ] },
          { "tag_name": "v0.4.0", "draft": true, "prerelease": false, "html_url": "https://github.com/x/draft", "assets": [] },
          { "tag_name": "v0.2.0", "draft": false, "prerelease": false, "html_url": "https://github.com/x/releases/tag/v0.2.0", "body": "",
            "assets": [
              { "name": "MorniLAN-Admin-Setup-0.2.0.exe", "browser_download_url": "https://github.com/x/a2.exe", "size": 90 },
              { "name": "MorniLAN-Geraete-Setup-0.2.0.exe", "browser_download_url": "https://github.com/x/g2.exe", "size": 190 }
            ] },
          { "tag_name": "nightly", "draft": false, "prerelease": true, "html_url": "https://github.com/x/n", "assets": [] }
        ]
        """;

    private static IReadOnlyList<ReleaseInfo> Releases() => GitHubReleases.Parse(JsonDocument.Parse(ApiResponse).RootElement);

    [Fact]
    public void Parse_SkipsDraftsAndInvalidTags_ReadsDigest()
    {
        var releases = Releases();
        Assert.Equal(["v0.3.0-beta.1", "v0.2.0"], releases.Select(r => r.Tag));
        Assert.Equal(new string('b', 64), releases[0].Assets[1].Sha256);
        Assert.Null(releases[1].Assets[0].Sha256);
    }

    [Fact]
    public void FindUpdate_PicksNewest_WithMatchingSetup()
    {
        var update = GitHubReleases.FindUpdate(Releases(), SemanticVersion.Parse("0.2.0"), UpdateProduct.Device, includePrereleases: true);
        Assert.NotNull(update);
        Assert.Equal("0.3.0-beta.1", update.Version.ToString());
        Assert.Equal("MorniLAN-Geraete-Setup-0.3.0-beta.1.exe", update.Setup.Name);
    }

    [Fact]
    public void FindUpdate_IgnoresBetas_WhenNotWanted()
    {
        Assert.Null(GitHubReleases.FindUpdate(Releases(), SemanticVersion.Parse("0.2.0"), UpdateProduct.Admin, includePrereleases: false));
    }

    [Fact]
    public void FindUpdate_NothingNewer()
    {
        Assert.Null(GitHubReleases.FindUpdate(Releases(), SemanticVersion.Parse("0.3.0-beta.1"), UpdateProduct.Admin, true));
        Assert.Null(GitHubReleases.FindUpdate(Releases(), SemanticVersion.Parse("0.3.0"), UpdateProduct.Admin, true));
    }

    [Fact]
    public void FindUpdate_FromBetaToBeta()
    {
        var update = GitHubReleases.FindUpdate(Releases(), SemanticVersion.Parse("0.2.0-beta.3"), UpdateProduct.Admin, true);
        Assert.Equal("0.3.0-beta.1", update!.Version.ToString());
    }
}
