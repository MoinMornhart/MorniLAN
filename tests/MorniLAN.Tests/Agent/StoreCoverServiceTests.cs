using System.Net;
using System.Net.Http.Headers;
using MorniLAN.Agent.Inventory;
using MorniLAN.Shared.Models;

namespace MorniLAN.Tests.Agent;

public sealed class StoreCoverServiceTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("mornilan-covers");
    private readonly FakeStore _store = new();
    private DateTimeOffset _now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _dir.Delete(recursive: true);

    private StoreCoverService Service() => new(_dir.FullName, _store, () => _now);

    private static InventoryItem Game(string name, string launcher = GameLaunchers.Ubisoft) =>
        new(new AppEntry($"x:{name}", name, AppSource.InstalledProgram, Launcher: launcher), new ImageSources());

    // So antwortet der Shop wirklich (gekürzt), inklusive DLC mit ähnlichem Namen
    private const string ValhallaSearch = """
        {"total":3,"items":[
          {"type":"app","name":"Assassin's Creed® Valhalla - Season Pass","id":2210740},
          {"type":"app","name":"Assassin's Creed Valhalla","id":2208920},
          {"type":"app","name":"Assassin's Creed® Valhalla - Dawn of Ragnarök","id":2210140}]}
        """;

    [Fact]
    public void Match_IgnoresTrademarksAndApostropheStyle_ButNotDlcNames()
    {
        Assert.Equal(2208920, StoreCoverService.MatchSearchResult(ValhallaSearch, "Assassin’s Creed® Valhalla"));
        Assert.Null(StoreCoverService.MatchSearchResult(ValhallaSearch, "Assassin's Creed Odyssey"));
        Assert.Null(StoreCoverService.MatchSearchResult("""{"total":0,"items":[]}""", "Irgendwas"));
    }

    // Echte Antwort für „The Witcher 3: Wild Hunt“ (gekürzt): das Spiel heißt auf Steam inzwischen „— Remastered“
    private const string WitcherSearch = """
        {"total":4,"items":[
          {"type":"app","name":"The Witcher 3: Wild Hunt — Songs of the Past","id":5006530},
          {"type":"app","name":"The Witcher 3: Wild Hunt — Remastered","id":292030},
          {"type":"app","name":"The Witcher 3: Wild Hunt — Remastered Soundtrack","id":1229320}]}
        """;

    [Theory]
    [InlineData("The Witcher 3: Wild Hunt", 292030)]
    [InlineData("The Witcher 3: Wild Hunt - Game of the Year Edition", 292030)] // so heißt es bei GOG
    [InlineData("The Witcher 3: Wild Hunt — Songs of the Past", 5006530)] // exakt bleibt exakt
    [InlineData("The Witcher 3", null)] // kürzerer Name passt nicht auf etwas Längeres
    public void Match_ToleratesEditionSuffixes_ButNotDlc(string local, int? expected)
    {
        Assert.Equal(expected, StoreCoverService.MatchSearchResult(WitcherSearch, local));
    }

    [Theory]
    [InlineData("The Witcher 3: Wild Hunt - Game of the Year Edition", "The Witcher 3: Wild Hunt")]
    [InlineData("Mortal Kombat 11 Ultimate Edition", "Mortal Kombat 11")]
    [InlineData("Pokémon Gold", "Pokémon Gold")] // „Gold“ allein ist Teil des Namens
    [InlineData("Assassin’s Creed® Valhalla", "Assassin's Creed Valhalla")]
    public void SearchTerm_DropsTrademarksAndEditions(string name, string expected)
    {
        Assert.Equal(expected, StoreCoverService.SearchTerm(name));
    }

    [Fact]
    public async Task LauncherGame_GetsPortraitCover_AndIsCachedForNextRun()
    {
        _store.Search["Assassin's Creed Valhalla"] = ValhallaSearch;
        _store.Images["/2208920/library_600x900.jpg"] = FakeStore.Jpeg(50_000);

        var first = await Service().FillAsync([Game("Assassin’s Creed® Valhalla")], TestContext.Current.CancellationToken);
        var cover = Assert.Single(first).Images.CoverFile;
        Assert.NotNull(cover);
        Assert.Equal(50_000, new FileInfo(cover).Length);

        var requests = _store.Requests.Count;
        var second = await Service().FillAsync([Game("Assassin’s Creed® Valhalla")], TestContext.Current.CancellationToken);
        Assert.Equal(cover, Assert.Single(second).Images.CoverFile);
        Assert.Equal(requests, _store.Requests.Count); // nichts mehr angefragt
    }

    [Fact]
    public async Task PlaceholderPortrait_FallsBackToHeaderFromDetails()
    {
        _store.Search["Battlefield 6"] = """{"items":[{"type":"app","name":"Battlefield™ 6","id":2807960}]}""";
        _store.Images["/2807960/library_600x900.jpg"] = FakeStore.Jpeg(1655); // Platzhalter, wie bei neuen Spielen
        _store.Details[2807960] = """{"2807960":{"success":true,"data":{"header_image":"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/2807960/abc/header.jpg?t=1"}}}""";
        _store.Images["/2807960/abc/header.jpg"] = FakeStore.Jpeg(39_851);

        var item = Assert.Single(await Service().FillAsync([Game("Battlefield™ 6", GameLaunchers.Ea)], TestContext.Current.CancellationToken));

        Assert.Equal(39_851, new FileInfo(item.Images.CoverFile!).Length);
    }

    [Fact]
    public async Task SteamGameWithoutLocalCover_IsLoadedByAppId_WithoutSearch()
    {
        _store.Images["/427520/library_600x900.jpg"] = FakeStore.Jpeg(100_000);
        var factorio = new InventoryItem(new AppEntry("steam:427520", "Factorio", AppSource.Steam, SteamAppId: 427520), new ImageSources());

        var item = Assert.Single(await Service().FillAsync([factorio], TestContext.Current.CancellationToken));

        Assert.EndsWith("steam-427520.jpg", item.Images.CoverFile);
        Assert.DoesNotContain(_store.Requests, r => r.Contains("storesearch"));
    }

    [Fact]
    public async Task NotFound_IsRememberedForSevenDays()
    {
        _store.Search["Kleines Indie-Spiel"] = """{"total":0,"items":[]}""";

        Assert.Null(Assert.Single(await Service().FillAsync([Game("Kleines Indie-Spiel")], TestContext.Current.CancellationToken)).Images.CoverFile);
        var afterFirst = _store.Requests.Count;

        _now = _now.AddDays(6);
        await Service().FillAsync([Game("Kleines Indie-Spiel")], TestContext.Current.CancellationToken);
        Assert.Equal(afterFirst, _store.Requests.Count);

        _now = _now.AddDays(2);
        await Service().FillAsync([Game("Kleines Indie-Spiel")], TestContext.Current.CancellationToken);
        Assert.Equal(afterFirst + 1, _store.Requests.Count);
    }

    [Fact]
    public async Task Offline_StopsAfterFirstFailure_AndRemembersNothing()
    {
        _store.Offline = true;

        var items = await Service().FillAsync([Game("Spiel A"), Game("Spiel B"), Game("Spiel C")], TestContext.Current.CancellationToken);

        Assert.All(items, i => Assert.Null(i.Images.CoverFile));
        Assert.Single(_store.Requests);
        Assert.False(File.Exists(Path.Combine(_dir.FullName, "misses.txt")) &&
                     File.ReadAllText(Path.Combine(_dir.FullName, "misses.txt")).Length > 0);

        _store.Offline = false;
        _store.Search["Spiel A"] = """{"items":[]}""";
        await Service().FillAsync([Game("Spiel A")], TestContext.Current.CancellationToken);
        Assert.Contains(_store.Requests.Skip(1), r => r.Contains("Spiel%20A"));
    }

    [Fact]
    public async Task ProgramsAndGamesWithLocalCover_AreLeftAlone()
    {
        var local = Path.Combine(_dir.FullName, "local.jpg");
        File.WriteAllBytes(local, FakeStore.Jpeg(10_000));
        InventoryItem[] items =
        [
            new(new AppEntry("exe:1", "Discord", AppSource.InstalledProgram), new ImageSources()),
            new(new AppEntry("steam:1", "Mit Cover", AppSource.Steam, SteamAppId: 1), new ImageSources(CoverFile: local)),
        ];

        var result = await Service().FillAsync(items, TestContext.Current.CancellationToken);

        Assert.Equal(items, result);
        Assert.Empty(_store.Requests);
    }

    [Fact]
    public async Task LookupsPerRun_AreLimited()
    {
        var games = Enumerable.Range(1, StoreCoverService.MaxLookupsPerRun + 5).Select(i => Game($"Spiel {i}")).ToList();

        await Service().FillAsync(games, TestContext.Current.CancellationToken);

        Assert.Equal(StoreCoverService.MaxLookupsPerRun, _store.Requests.Count);
    }

    /// <summary>Steam-Shop zum Nachspielen: Suche, Shop-Details und Bilder.</summary>
    private sealed class FakeStore : HttpMessageHandler
    {
        public Dictionary<string, string> Search { get; } = [];
        public Dictionary<int, string> Details { get; } = [];
        public Dictionary<string, byte[]> Images { get; } = [];
        public List<string> Requests { get; } = [];
        public bool Offline { get; set; }

        public static byte[] Jpeg(int size)
        {
            var data = new byte[size];
            data[0] = 0xFF;
            data[1] = 0xD8;
            return data;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri.AbsoluteUri);
            if (Offline)
                throw new HttpRequestException("Kein Netz");

            if (uri.AbsolutePath == "/api/storesearch/")
            {
                var term = Uri.UnescapeDataString(uri.Query.Split('&')[0]["?term=".Length..]);
                return Json(Search.GetValueOrDefault(term, """{"total":0,"items":[]}"""));
            }
            if (uri.AbsolutePath == "/api/appdetails")
            {
                var id = int.Parse(uri.Query.Split('&')[0]["?appids=".Length..]);
                return Json(Details.GetValueOrDefault(id, $$$"""{"{{{id}}}":{"success":false}}"""));
            }
            const string prefix = "/store_item_assets/steam/apps";
            if (uri.AbsolutePath.StartsWith(prefix) && Images.TryGetValue(uri.AbsolutePath[prefix.Length..], out var image))
            {
                var content = new ByteArrayContent(image);
                content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("nope") });
        }

        private static Task<HttpResponseMessage> Json(string json) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
