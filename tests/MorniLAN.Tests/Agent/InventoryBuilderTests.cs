using MorniLAN.Agent.Inventory;
using MorniLAN.Shared.Models;

namespace MorniLAN.Tests.Agent;

public class InventoryBuilderTests
{
    private static readonly SteamGame Factorio = new(427520, "Factorio", @"C:\Steam\steamapps\common\Factorio", 2249816030,
        @"C:\Steam\appcache\librarycache\427520\library_600x900.jpg", null);

    private static IReadOnlyList<InventoryItem> Build(
        IReadOnlyList<UninstallEntry>? uninstall = null,
        IReadOnlyList<StartMenuShortcut>? shortcuts = null,
        IReadOnlyList<StoreApp>? store = null,
        IReadOnlyList<SteamGame>? steam = null) =>
        InventoryBuilder.Build(steam ?? [], uninstall ?? [], shortcuts ?? [], store ?? []);

    [Fact]
    public void SteamGame_AppearsOnce_EvenWithSteamUninstallKey_AndShortcut()
    {
        var items = Build(
            steam: [Factorio],
            uninstall: [new UninstallEntry("Steam App 427520", "Factorio", "Wube Software")],
            shortcuts: [new StartMenuShortcut("Factorio", @"C:\Steam\steamapps\common\Factorio\bin\x64\factorio.exe", null, "x.lnk")]);

        var item = Assert.Single(items);
        Assert.Equal("steam:427520", item.App.Id);
        Assert.Equal("steam://rungameid/427520", item.App.LaunchTarget);
        Assert.EndsWith("library_600x900.jpg", item.Images.CoverFile);
    }

    [Fact]
    public void Shortcut_InInstallFolder_ProvidesExecutable_AndIsNotDuplicated()
    {
        var items = Build(
            uninstall: [new UninstallEntry("Discord", "Discord", "Discord Inc.", "1.0.9", @"C:\Users\f\AppData\Local\Discord",
                @"C:\Users\f\AppData\Local\Discord\app.ico", 300_000)],
            shortcuts: [new StartMenuShortcut("Discord", @"C:\Users\f\AppData\Local\Discord\Update.exe",
                "--processStart Discord.exe", "Discord.lnk")]);

        var item = Assert.Single(items);
        Assert.Equal(@"C:\Users\f\AppData\Local\Discord\Update.exe", item.App.ExecutablePath);
        Assert.Equal("--processStart Discord.exe", item.App.LaunchArguments);
        Assert.Equal(AppId.ForExecutable(@"C:\Users\f\AppData\Local\Discord\Update.exe"), item.App.Id);
        Assert.Equal("Discord Inc.", item.App.Publisher);
        Assert.False(item.App.IsSystemComponent);
    }

    [Fact]
    public void ProgramWithoutExe_GetsIdFromUninstallKey_AndUninstallerIsNotUsed()
    {
        var items = Build(uninstall: [new UninstallEntry("{1234-ABCD}", "Tool", DisplayIcon: @"C:\Tool\unins000.exe,0")]);

        var item = Assert.Single(items);
        Assert.Null(item.App.ExecutablePath);
        Assert.Equal(AppId.ForUninstallKey("{1234-ABCD}"), item.App.Id);
    }

    [Fact]
    public void ExeFromDisplayIcon_WhenNoShortcut()
    {
        var items = Build(uninstall: [new UninstallEntry("VLC", "VLC media player", DisplayIcon: "\"C:\\Program Files\\VideoLAN\\VLC\\vlc.exe\",0")]);
        Assert.Equal(@"C:\Program Files\VideoLAN\VLC\vlc.exe", Assert.Single(items).App.ExecutablePath);
    }

    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810", false, null, null)]
    [InlineData("NVIDIA Grafiktreiber 560.94", false, null, null)]
    [InlineData("Irgendwas", true, null, null)]
    [InlineData("Update für Office", false, "Office16", null)]
    [InlineData("KB5034441", false, null, "Security Update")]
    public void SystemComponents_AreFlagged(string name, bool systemComponent, string? parent, string? releaseType)
    {
        var items = Build(uninstall: [new UninstallEntry("k", name, SystemComponent: systemComponent, ParentKeyName: parent,
            ReleaseType: releaseType)]);
        Assert.True(Assert.Single(items).App.IsSystemComponent);
    }

    [Theory]
    [InlineData("Discord")]
    [InlineData("Mozilla Firefox (x64 de)")]
    [InlineData("Steam")]
    [InlineData("VLC media player")]
    public void NormalPrograms_AreNotSystem(string name)
    {
        Assert.False(SystemComponents.IsSystem(new UninstallEntry("k", name)));
        Assert.False(Assert.Single(Build(uninstall: [new UninstallEntry("k", name, DisplayIcon: @"D:\Apps\x\app.exe")]))
            .App.IsSystemComponent);
    }

    [Fact]
    public void StandaloneShortcut_BecomesEntry_WindowsToolsCountAsSystem()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var items = Build(shortcuts:
        [
            new StartMenuShortcut("Minecraft Launcher", @"C:\XboxGames\Minecraft Launcher\Minecraft.exe", null, "m.lnk"),
            new StartMenuShortcut("Remotedesktopverbindung", Path.Combine(windows, @"System32\mstsc.exe"), null, "r.lnk"),
        ]);

        Assert.False(items.Single(i => i.App.Name == "Minecraft Launcher").App.IsSystemComponent);
        Assert.True(items.Single(i => i.App.Name == "Remotedesktopverbindung").App.IsSystemComponent);
    }

    [Fact]
    public void StoreApp_LaunchesViaAppsFolder()
    {
        var items = Build(store: [new StoreApp("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify", "Spotify AB", "1.2.3.0",
            @"C:\Apps\Spotify\logo.png", null)]);

        var app = Assert.Single(items).App;
        Assert.Equal(AppSource.StoreApp, app.Source);
        Assert.Equal("store:spotifyab.spotifymusic_zpdnekdrzrea0!spotify", app.Id);
        Assert.Equal(@"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", app.LaunchTarget);
    }

    [Fact]
    public void Result_IsSortedByName_AndHasNoDuplicateIds()
    {
        var items = Build(
            uninstall: [new UninstallEntry("b", "Zoom", DisplayIcon: @"C:\Zoom\zoom.exe"), new UninstallEntry("a", "Audacity")],
            shortcuts: [new StartMenuShortcut("Zoom Workplace", @"C:\Zoom\zoom.exe", null, "z.lnk")]);

        Assert.Equal(["Audacity", "Zoom"], items.Select(i => i.App.Name));
    }

    // --- Fälle aus dem echten Lauf auf MORNI ---------------------------------------------------

    [Fact]
    public void NoDuplicate_WhenEntryHasNoInstallLocation_ButIconInSameFolder()
    {
        // Google Chrome: kein InstallLocation, aber DisplayIcon zeigt in den Programmordner
        var items = Build(
            uninstall: [new UninstallEntry("Google Chrome", "Google Chrome", "Google LLC",
                DisplayIcon: @"C:\Program Files\Google\Chrome\Application\chrome.exe,0")],
            shortcuts: [new StartMenuShortcut("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", null, "c.lnk")]);

        var item = Assert.Single(items);
        Assert.Equal("Google LLC", item.App.Publisher);
    }

    [Fact]
    public void NoDuplicate_WhenOnlyUninstallerShowsTheFolder()
    {
        var items = Build(
            uninstall: [new UninstallEntry("Netbird", "Netbird", UninstallString: "\"C:\\Program Files\\Netbird\\netbird_uninstall.exe\"")],
            shortcuts: [new StartMenuShortcut("Netbird", @"C:\Program Files\Netbird\Netbird-ui.exe", null, "n.lnk")]);

        var item = Assert.Single(items);
        Assert.Equal(@"C:\Program Files\Netbird\Netbird-ui.exe", item.App.ExecutablePath);
    }

    [Fact]
    public void NoDuplicate_ByName_WhenNoFolderIsKnown()
    {
        var items = Build(
            uninstall: [new UninstallEntry("{GUID}", "Oracle VirtualBox 7.2.20", "Oracle")],
            shortcuts: [new StartMenuShortcut("Oracle VirtualBox", @"D:\VB\VirtualBox.exe", null, "v.lnk")]);

        var item = Assert.Single(items);
        Assert.Equal("Oracle VirtualBox 7.2.20", item.App.Name);
        Assert.Equal(@"D:\VB\VirtualBox.exe", item.App.ExecutablePath);
    }

    [Fact]
    public void ExactName_WinsOverPrefix()
    {
        // "Microsoft Edge" darf nicht beim Eintrag "Microsoft Edge WebView2-Laufzeit" landen
        var items = Build(
            uninstall:
            [
                new UninstallEntry("WebView", "Microsoft Edge WebView2-Laufzeit"),
                new UninstallEntry("Edge", "Microsoft Edge"),
            ],
            shortcuts: [new StartMenuShortcut("Microsoft Edge", @"X:\Edge\msedge.exe", null, "e.lnk")]);

        Assert.Equal(@"X:\Edge\msedge.exe", items.Single(i => i.App.Name == "Microsoft Edge").App.ExecutablePath);
        Assert.True(items.Single(i => i.App.Name.Contains("WebView2")).App.IsSystemComponent);
    }

    [Fact]
    public void Suite_WithSeveralShortcuts_ShowsEachProgram()
    {
        var items = Build(
            uninstall: [new UninstallEntry("O365", "Microsoft 365 - de-de", "Microsoft Corporation",
                InstallLocation: @"C:\Program Files\Microsoft Office")],
            shortcuts:
            [
                new StartMenuShortcut("Word", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", null, "w.lnk"),
                new StartMenuShortcut("Excel", @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE", null, "x.lnk"),
            ]);

        Assert.Equal(["Excel", "Word"], items.Select(i => i.App.Name));
        Assert.All(items, i => Assert.Equal("Microsoft Corporation", i.App.Publisher));
    }

    [Fact]
    public void SetupExe_IsNotTheProgram()
    {
        // OneDrive meldet OneDriveSetup.exe als Icon, gestartet wird OneDrive.exe aus der Verknüpfung
        var items = Build(
            uninstall: [new UninstallEntry("OneDriveSetup.exe", "Microsoft OneDrive",
                DisplayIcon: @"C:\Users\f\AppData\Local\Microsoft\OneDrive\26.1\OneDriveSetup.exe,-101")],
            shortcuts: [new StartMenuShortcut("OneDrive", @"C:\Users\f\AppData\Local\Microsoft\OneDrive\OneDrive.exe", null, "o.lnk")]);

        var item = Assert.Single(items);
        Assert.Equal(@"C:\Users\f\AppData\Local\Microsoft\OneDrive\OneDrive.exe", item.App.ExecutablePath);
    }

    [Fact]
    public void DoubleBackslash_InPath_GivesSameEntry()
    {
        var items = Build(
            uninstall: [new UninstallEntry("Logi", "Logi Download Assistant",
                DisplayIcon: @"C:\Program Files\LogiDownloadAssistant\\bin\logi_download_assistant.exe")],
            shortcuts: [new StartMenuShortcut("Logi Download Assistant", @"C:\Program Files\LogiDownloadAssistant\bin\logi_download_assistant.exe", null, "l.lnk")]);
        Assert.Single(items);
    }

    [Fact]
    public void SecondEntryWithoutExe_IsDropped_WhenProgramIsLaunchable()
    {
        var items = Build(
            uninstall:
            [
                new UninstallEntry("PPRO_26_0", "Adobe Premiere Pro 2026"),
                new UninstallEntry("{ADOBE}", "Adobe Premiere Pro 2026", DisplayIcon: @"C:\Adobe\Premiere\Adobe Premiere Pro.exe"),
            ]);
        Assert.Equal(@"C:\Adobe\Premiere\Adobe Premiere Pro.exe", Assert.Single(items).App.ExecutablePath);
    }

    [Fact]
    public void ProgramWithoutAnyExe_IsHiddenAsSystem()
    {
        Assert.True(Assert.Single(Build(uninstall: [new UninstallEntry("q", "QEMU")])).App.IsSystemComponent);
    }

    [Fact]
    public void MorniLanItself_IsHidden()
    {
        var items = Build(uninstall: [new UninstallEntry("{8C2F}", "MorniLAN für Geräte",
            DisplayIcon: @"C:\Program Files\MorniLAN\Launcher\MorniLAN.Launcher.exe")]);
        Assert.True(Assert.Single(items).App.IsSystemComponent);
    }

    [Theory]
    [InlineData("RealtekSemiconductorCorp.RealtekAudioControl_dt26b99r8h8gj!App", "Realtek Audio Console", "Realtek Semiconductor Corp", true)]
    [InlineData("Microsoft.WindowsFeedbackHub_8wekyb3d8bbwe!App", "Feedback-Hub", "Microsoft Corporation", true)]
    [InlineData("Microsoft.WindowsStore_8wekyb3d8bbwe!App", "Microsoft Store", "Microsoft Corporation", true)]
    [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", "WhatsApp", "WhatsApp Inc.", false)]
    [InlineData("Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App", "XBOX", "Microsoft Corporation", false)]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Windows-Rechner", "Microsoft Corporation", false)]
    public void StoreApps_SystemOrNot(string aumid, string name, string publisher, bool system)
    {
        var items = Build(store: [new StoreApp(aumid, name, publisher, "1.0.0.0", null, null)]);
        Assert.Equal(system, Assert.Single(items).App.IsSystemComponent);
    }

    [Theory]
    [InlineData("Oracle VirtualBox 7.2.20", "Oracle VirtualBox", true)]
    [InlineData("Microsoft OneDrive", "OneDrive", true)]
    [InlineData("Git", "Git CMD", true)]
    [InlineData("Google Chrome", "Google Chrome", true)]
    [InlineData("Steam", "Stellaris", false)]
    [InlineData("Microsoft Edge", "Microsoft Excel", false)]
    [InlineData("Go", "Go", false)] // zu kurz, zu unsicher
    public void NamesMatch_Cases(string a, string b, bool expected)
    {
        Assert.Equal(expected, PathText.NamesMatch(a, b));
    }

    [Theory]
    [InlineData("\"C:\\x\\app.exe\",0", @"C:\x\app.exe")]
    [InlineData(@"C:\x\app.exe", @"C:\x\app.exe")]
    [InlineData(@"C:\x\app.ico", null)]
    [InlineData(@"C:\x\app.exe,-101", @"C:\x\app.exe")]
    [InlineData(null, null)]
    public void ExecutableFromIcon_ParsesRegistryFormat(string? icon, string? expected)
    {
        Assert.Equal(expected, PathText.ExecutableFromIcon(icon));
    }

    [Theory]
    [InlineData("S-1-5-21-123-456-789-1001", true)]
    [InlineData("S-1-5-21-123-456-789-1001_Classes", false)]
    [InlineData("S-1-5-18", false)]
    [InlineData(".DEFAULT", false)]
    public void UserSid_IsRecognized(string name, bool expected)
    {
        Assert.Equal(expected, UninstallRegistryScanner.IsUserSid(name));
    }
}
