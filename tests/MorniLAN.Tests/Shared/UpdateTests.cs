using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MorniLAN.Shared.Updates;
using MorniLAN.Tests.Connection;

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

    // ───── ETag-Caching / GitHub schonen ─────

    private sealed class StubHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        public List<string?> SentIfNoneMatch { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentIfNoneMatch.Add(request.Headers.IfNoneMatch.FirstOrDefault()?.Tag);
            return Task.FromResult(responses.Dequeue());
        }
    }

    private static HttpResponseMessage Ok(string body, string etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
        response.Headers.ETag = new EntityTagHeaderValue(etag, isWeak: true);
        return response;
    }

    [Fact]
    public async Task Fetch_CachesWithEtag_AndReusesOn304()
    {
        using var dir = new TempDirectory();
        var ct = TestContext.Current.CancellationToken;
        var handler = new StubHandler(new Queue<HttpResponseMessage>(
        [
            Ok(ApiResponse, "\"etag-1\""),
            new HttpResponseMessage(HttpStatusCode.NotModified),
        ]));
        using var http = new HttpClient(handler);

        var first = await GitHubReleases.FetchAsync(http, ct, dir.Path);
        var second = await GitHubReleases.FetchAsync(http, ct, dir.Path);

        Assert.Equal(["v0.3.0-beta.1", "v0.2.0"], first.Select(r => r.Tag));
        Assert.Equal(first.Select(r => r.Tag), second.Select(r => r.Tag)); // aus dem Cache, trotz 304
        Assert.Null(handler.SentIfNoneMatch[0]); // erste Abfrage ohne ETag
        Assert.Equal("\"etag-1\"", handler.SentIfNoneMatch[1]); // zweite schickt den gemerkten ETag
    }

    [Fact]
    public async Task Fetch_OnRateLimit_UsesCachedInsteadOfThrowing()
    {
        using var dir = new TempDirectory();
        var ct = TestContext.Current.CancellationToken;
        var limited = new HttpResponseMessage(HttpStatusCode.Forbidden);
        limited.Headers.Add("X-RateLimit-Remaining", "0");
        var handler = new StubHandler(new Queue<HttpResponseMessage>([Ok(ApiResponse, "\"etag-1\""), limited]));
        using var http = new HttpClient(handler);

        await GitHubReleases.FetchAsync(http, ct, dir.Path); // füllt den Cache
        var fallback = await GitHubReleases.FetchAsync(http, ct, dir.Path); // 403, aber Cache rettet

        Assert.Equal(["v0.3.0-beta.1", "v0.2.0"], fallback.Select(r => r.Tag));
    }

    [Fact]
    public async Task Fetch_WithoutCacheDir_StillWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new StubHandler(new Queue<HttpResponseMessage>([Ok(ApiResponse, "\"etag-1\"")]));
        using var http = new HttpClient(handler);

        var releases = await GitHubReleases.FetchAsync(http, ct, cacheDirectory: null);
        Assert.Equal(2, releases.Count);
    }

    [Fact]
    public async Task Fetch_WithCorruptCache_DiscardsIt_AndFetchesFresh()
    {
        using var dir = new TempDirectory();
        var ct = TestContext.Current.CancellationToken;
        // Kaputter Cache: gültige ETag-Zeile, abgeschnittener JSON-Rumpf
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "releases-cache.json"), "\"etag-1\"\n[ { \"tag_name\":", ct);
        // Weil der Cache verworfen wird, darf KEIN If-None-Match geschickt werden, und 200 liefert frisch
        var handler = new StubHandler(new Queue<HttpResponseMessage>([Ok(ApiResponse, "\"etag-2\"")]));
        using var http = new HttpClient(handler);

        var releases = await GitHubReleases.FetchAsync(http, ct, dir.Path);

        Assert.Equal(["v0.3.0-beta.1", "v0.2.0"], releases.Select(r => r.Tag));
        Assert.Null(handler.SentIfNoneMatch[0]); // korrupter Cache verworfen → ohne ETag neu geladen
    }
}
