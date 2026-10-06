using System.Text.Json;
using MorniLAN.Agent.Inventory;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Tests.Agent;

public class LauncherScannerTests
{
    // Aufbau wie ein echtes Manifest des Epic Games Launchers (gekürzt)
    private const string EpicManifest = """
        {
          "FormatVersion": 0,
          "bIsIncompleteInstall": false,
          "LaunchCommand": "",
          "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteLauncher.exe",
          "AppCategories": ["public", "games", "applications"],
          "DisplayName": "Fortnite",
          "InstallLocation": "D:\\Epic Games\\Fortnite",
          "InstallSize": 98765432100,
          "CatalogNamespace": "fn",
          "CatalogItemId": "4fe75bbc5a674f4f9b356b5c90567da5",
          "AppName": "Fortnite",
          "MainGameAppName": "Fortnite"
        }
        """;

    [Fact]
    public void Epic_Manifest_GivesGameWithLauncherUri()
    {
        var game = EpicScanner.Parse(EpicManifest);

        Assert.NotNull(game);
        Assert.Equal(GameLaunchers.Epic, game.Launcher);
        Assert.Equal("Fortnite", game.Name);
        Assert.Equal(@"D:\Epic Games\Fortnite", game.InstallDirectory);
        Assert.Equal(@"D:\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteLauncher.exe", game.ExecutablePath);
        Assert.Equal("com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true",
            game.LaunchUri);
        Assert.Equal(98765432100, game.SizeBytes);
    }

    [Theory]
    [InlineData("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": true")] // noch nicht fertig geladen
    [InlineData("\"MainGameAppName\": \"Fortnite\"", "\"MainGameAppName\": \"OtherGame\"")] // DLC
    [InlineData("[\"public\", \"games\", \"applications\"]", "[\"public\", \"engines\"]")] // Unreal Engine
    public void Epic_SkipsIncompleteDlcAndEngines(string original, string replacement)
    {
        Assert.Null(EpicScanner.Parse(EpicManifest.Replace(original, replacement)));
    }

    [Fact]
    public void Epic_Scan_SkipsBrokenManifests()
    {
        var dir = Directory.CreateTempSubdirectory("mornilan-epic");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "A.item"), EpicManifest);
            File.WriteAllText(Path.Combine(dir.FullName, "B.item"), "{ kaputt");
            File.WriteAllText(Path.Combine(dir.FullName, "C.txt"), EpicManifest);

            Assert.Equal("Fortnite", Assert.Single(EpicScanner.Scan(dir.FullName)).Name);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static Func<string, string?> Values(Dictionary<string, string> values) => name => values.GetValueOrDefault(name);

    [Fact]
    public void Gog_RegistryValues_GiveDirectlyStartableGame()
    {
        var dir = Directory.CreateTempSubdirectory("mornilan-gog");
        try
        {
            File.WriteAllBytes(Path.Combine(dir.FullName, "goggame-1207658924.ico"), [0, 0, 1, 0]);
            var game = GogScanner.Parse("1207658924", Values(new()
            {
                ["gameID"] = "1207658924",
                ["gameName"] = "The Witcher 3: Wild Hunt",
                ["path"] = dir.FullName,
                ["exe"] = Path.Combine(dir.FullName, @"bin\x64\witcher3.exe"),
                ["launchParam"] = "-skipintro",
            }));

            Assert.NotNull(game);
            Assert.Equal(GameLaunchers.Gog, game.Launcher);
            Assert.Equal("1207658924", game.LauncherId);
            Assert.Null(game.LaunchUri); // DRM-frei: direkt über die EXE
            Assert.Equal("-skipintro", game.LaunchArguments);
            Assert.EndsWith("goggame-1207658924.ico", game.IconFile);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Gog_Dlc_IsSkipped()
    {
        Assert.Null(GogScanner.Parse("1", Values(new()
        {
            ["gameName"] = "Witcher 3 - Hearts of Stone", ["path"] = @"C:\GOG Games\The Witcher 3",
            ["exe"] = @"C:\GOG Games\The Witcher 3\bin\x64\witcher3.exe", ["dependsOn"] = "1207658924",
        })));
    }

    [Fact]
    public void Ubisoft_FixesSlashes_AndUsesUplayUri()
    {
        var game = UbisoftScanner.Parse("13504", "C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Assassin's Creed Valhalla/",
            "Assassin's Creed® Valhalla", null);

        Assert.NotNull(game);
        Assert.Equal(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Assassin's Creed Valhalla", game.InstallDirectory);
        Assert.Equal("uplay://launch/13504/0", game.LaunchUri);
        Assert.Equal("Assassin's Creed® Valhalla", game.Name);
    }

    [Fact]
    public void Ubisoft_WithoutName_UsesFolder_AndRejectsBadIds()
    {
        Assert.Equal("Far Cry 6", UbisoftScanner.Parse("5266", @"D:\Ubisoft\Far Cry 6", null, null)?.Name);
        Assert.Null(UbisoftScanner.Parse("abc", @"D:\Ubisoft\X", "X", null));
        Assert.Null(UbisoftScanner.Parse("1", null, "X", null));
    }

    [Theory]
    [InlineData("Battlefield 2042", @"""C:\Program Files\Common Files\EAInstaller\Battlefield 2042\Cleanup.exe"" uninstall_game -autologging", GameLaunchers.Ea)]
    [InlineData("World of Warcraft", @"""C:\ProgramData\Battle.net\Agent\Blizzard Uninstaller.exe"" --lang=deDE --uid=wow --displayname=""World of Warcraft""", GameLaunchers.BattleNet)]
    [InlineData("Battle.net", @"""C:\ProgramData\Battle.net\Agent\Blizzard Uninstaller.exe"" --lang=deDE --uid=battle.net --displayname=""Battle.net""", null)]
    [InlineData("EA app", @"MsiExec.exe /X{ABCDEF}", null)]
    [InlineData("Discord", @"C:\Users\f\AppData\Local\Discord\Update.exe --uninstall", null)]
    public void LauncherDetection_RecognizesEaAndBattleNetGames(string name, string uninstall, string? expected)
    {
        Assert.Equal(expected, LauncherDetection.ForUninstallEntry(new UninstallEntry("k", name, UninstallString: uninstall)));
    }

    private static readonly LauncherGame Fortnite = EpicScanner.Parse(EpicManifest)!;

    [Fact]
    public void Builder_EpicGame_AppearsOnce_WithLauncherStart()
    {
        var items = InventoryBuilder.Build([],
            [new UninstallEntry("Fortnite", "Fortnite", "Epic Games", InstallLocation: @"D:\Epic Games\Fortnite")],
            [new StartMenuShortcut("Fortnite", @"D:\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient.exe", null, "x.lnk")],
            [], [Fortnite]);

        var item = Assert.Single(items);
        Assert.Equal("epic:fortnite", item.App.Id);
        Assert.True(item.App.IsGame);
        Assert.Equal(GameLaunchers.Epic, item.App.Launcher);
        Assert.StartsWith("com.epicgames.launcher://", item.App.LaunchTarget);
        Assert.False(item.App.IsSystemComponent);
    }

    [Fact]
    public void Builder_EaGame_FromProgramList_IsMarkedAsGame_LauncherItselfStaysProgram()
    {
        var items = InventoryBuilder.Build([],
        [
            new UninstallEntry("{EA-BF2042}", "Battlefield™ 2042", "Electronic Arts", InstallLocation: @"D:\EA Games\Battlefield 2042",
                UninstallString: @"""C:\Program Files\Common Files\EAInstaller\Battlefield 2042\Cleanup.exe"" uninstall_game"),
            new UninstallEntry("{EA-APP}", "EA app", "Electronic Arts", InstallLocation: @"C:\Program Files\Electronic Arts\EA Desktop",
                UninstallString: "MsiExec.exe /X{EA-APP}"),
        ],
        [
            new StartMenuShortcut("Battlefield 2042", @"D:\EA Games\Battlefield 2042\BF2042.exe", null, "bf.lnk"),
            new StartMenuShortcut("EA", @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EALauncher.exe", null, "ea.lnk"),
        ], []);

        var game = Assert.Single(items, i => i.App.Name == "Battlefield™ 2042");
        Assert.Equal(GameLaunchers.Ea, game.App.Launcher);
        Assert.True(game.App.IsGame);
        Assert.Equal(@"D:\EA Games\Battlefield 2042\BF2042.exe", game.App.LaunchTarget);
        var app = Assert.Single(items, i => i.App.Name == "EA app");
        Assert.False(app.App.IsGame);
    }

    [Fact]
    public void Builder_UninstallEntryInGameFolder_WithoutShortcut_IsDropped()
    {
        var gog = new LauncherGame(GameLaunchers.Gog, "gog", "1", "Cyberpunk 2077", @"C:\GOG Games\Cyberpunk 2077",
            @"C:\GOG Games\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe");
        var items = InventoryBuilder.Build([],
            [new UninstallEntry("1_is1", "Cyberpunk 2077", "GOG.com", InstallLocation: @"C:\GOG Games\Cyberpunk 2077\",
                DisplayIcon: @"C:\GOG Games\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe")],
            [], [], [gog]);

        Assert.Equal("gog:1", Assert.Single(items).App.Id);
    }

    [Fact]
    public void Json_KeepsOldFieldsStable_AndIgnoresUnknownFields()
    {
        var entry = new AppEntry("epic:fortnite", "Fortnite", AppSource.InstalledProgram, Launcher: GameLaunchers.Epic,
            LaunchUri: "com.epicgames.launcher://apps/x");

        var json = JsonSerializer.Serialize(entry, MorniLanJsonContext.Default.AppEntry);
        // Ein älteres Panel kennt nur die alten Source-Werte: hier darf kein neuer stehen
        Assert.Contains("\"source\":\"installedprogram\"", json.Replace(" ", "").ToLowerInvariant());
        Assert.DoesNotContain("isgame", json.ToLowerInvariant());

        var back = JsonSerializer.Deserialize(json, MorniLanJsonContext.Default.AppEntry)!;
        Assert.Equal(GameLaunchers.Epic, back.Launcher);
        Assert.Equal("com.epicgames.launcher://apps/x", back.LaunchTarget);

        // Umgekehrt: Felder aus einer späteren Version überliest diese Version
        var future = json.TrimEnd('}') + ",\"SomethingNew\":42}";
        Assert.Equal("Fortnite", JsonSerializer.Deserialize(future, MorniLanJsonContext.Default.AppEntry)!.Name);
    }
}
