using MorniLAN.Agent.Inventory;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Agent;

public class VdfTests
{
    [Fact]
    public void Parses_NestedNodes_Escapes_CommentsAndConditions()
    {
        const string text = """
            // Kommentar
            "libraryfolders"
            {
                "0"
                {
                    "path"      "C:\\Program Files (x86)\\Steam"
                    "label"     "Sag \"Hallo\""
                    "apps" { "427520" "2249816030" }
                }
                "1" [$WIN32] { "path" "D:\\SteamLibrary" }
            }
            """;

        var root = Vdf.Parse(text);
        var folders = root.Child("libraryfolders")!;
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders.Child("0")!["path"]);
        Assert.Equal("Sag \"Hallo\"", folders.Child("0")!["label"]);
        Assert.Equal("2249816030", folders.Child("0")!.Child("apps")!["427520"]);
        Assert.Equal(@"D:\SteamLibrary", folders.Child("1")!["path"]);
    }

    [Fact]
    public void KeysAreCaseInsensitive()
    {
        var root = Vdf.Parse("\"AppState\" { \"AppID\" \"10\" }");
        Assert.Equal("10", root.Child("appstate")!["appid"]);
    }

    [Theory]
    [InlineData("\"a\" {")]
    [InlineData("\"a\" \"unterminated")]
    [InlineData("}")]
    [InlineData("\"a\"")]
    public void Garbage_Throws_FormatException(string text)
    {
        Assert.Throws<FormatException>(() => Vdf.Parse(text));
    }
}

public sealed class SteamScannerTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private string Steam => Path.Combine(_dir.Path, "Steam");
    private string Library2 => Path.Combine(_dir.Path, "SteamLibrary");

    public void Dispose() => _dir.Dispose();

    // Echtes Manifest von MORNI (gekürzt)
    private const string FactorioManifest = """
        "AppState"
        {
            "appid"		"427520"
            "Universe"		"1"
            "LauncherPath"		"C:\\Program Files (x86)\\Steam\\steam.exe"
            "name"		"Factorio"
            "StateFlags"		"4"
            "installdir"		"Factorio"
            "SizeOnDisk"		"2249816030"
            "LastOwner"		"76561199528530909"
        }
        """;

    private void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void CreateSteam()
    {
        var lib2 = Library2.Replace(@"\", @"\\");
        Write(Path.Combine(Steam, "steamapps", "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
                "0" { "path" "{{Steam.Replace(@"\", @"\\")}}" }
                "1" { "path" "{{lib2}}" }
            }
            """);
        Write(Path.Combine(Steam, "steamapps", "appmanifest_427520.acf"), FactorioManifest);
        // Redistributables: kein Spiel
        Write(Path.Combine(Steam, "steamapps", "appmanifest_228980.acf"),
            "\"AppState\" { \"appid\" \"228980\" \"name\" \"Steamworks Common Redistributables\" \"StateFlags\" \"4\" }");
        // Wird gerade heruntergeladen (StateFlags 1026 = Update läuft, nicht fertig installiert)
        Write(Path.Combine(Library2, "steamapps", "appmanifest_730.acf"),
            "\"AppState\" { \"appid\" \"730\" \"name\" \"Counter-Strike 2\" \"StateFlags\" \"1026\" \"installdir\" \"CS2\" }");
        // Zweite Bibliothek, fertig installiert
        Write(Path.Combine(Library2, "steamapps", "appmanifest_570.acf"),
            "\"AppState\" { \"appid\" \"570\" \"name\" \"Dota 2\" \"StateFlags\" \"4\" \"installdir\" \"dota 2 beta\" \"SizeOnDisk\" \"1000\" }");
        // Kaputtes Manifest darf nichts kaputt machen
        Write(Path.Combine(Library2, "steamapps", "appmanifest_999.acf"), "\"AppState\" {");

        var cache = Path.Combine(Steam, "appcache", "librarycache");
        Write(Path.Combine(cache, "427520", "library_600x900.jpg"), "cover");
        Write(Path.Combine(cache, "427520", "267f5a89f36ab287e600a4e7d4e73d3d11f0fd7d.jpg"), "icon");
        Write(Path.Combine(cache, "427520", "a1331f002a798a9a41bde4acddfdaac612805bea", "library_header.jpg"), "header");
        Write(Path.Combine(cache, "570_header.jpg"), "alt-header"); // altes Layout, kein Cover
    }

    [Fact]
    public void Scan_FindsInstalledGamesInAllLibraries()
    {
        CreateSteam();

        var games = SteamScanner.Scan(Steam);

        Assert.Equal(["Dota 2", "Factorio"], games.Select(g => g.Name));
        var factorio = games.Single(g => g.AppId == 427520);
        Assert.Equal(2249816030, factorio.SizeOnDisk);
        Assert.Equal(Path.Combine(Steam, "steamapps", "common", "Factorio"), factorio.InstallDirectory);
        Assert.EndsWith("library_600x900.jpg", factorio.CoverPath);
        Assert.EndsWith("267f5a89f36ab287e600a4e7d4e73d3d11f0fd7d.jpg", factorio.IconPath);
    }

    [Fact]
    public void Scan_IgnoresRedistributables_Downloads_AndBrokenManifests()
    {
        CreateSteam();
        var ids = SteamScanner.Scan(Steam).Select(g => g.AppId).ToList();
        Assert.DoesNotContain(228980, ids);
        Assert.DoesNotContain(730, ids);
        Assert.DoesNotContain(999, ids);
    }

    [Fact]
    public void Cover_FallsBackToHeader_InOldLayout()
    {
        CreateSteam();
        var dota = SteamScanner.Scan(Steam).Single(g => g.AppId == 570);
        Assert.EndsWith("570_header.jpg", dota.CoverPath);
        Assert.Null(dota.IconPath);
    }

    [Fact]
    public void LibraryFolders_ReadsOldFormat_AndWithoutFile()
    {
        Assert.Equal([Steam], SteamScanner.LibraryFolders(Steam));

        Write(Path.Combine(Steam, "steamapps", "libraryfolders.vdf"),
            "\"LibraryFolders\" { \"TimeNextStatsReport\" \"123\" \"1\" \"D:\\\\Spiele\\\\Steam\" }");
        Assert.Equal([Steam, @"D:\Spiele\Steam"], SteamScanner.LibraryFolders(Steam));
    }
}
