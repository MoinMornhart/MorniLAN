using System.Text.Json;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Tests.Shared;

public class AppPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Default_AllowsEverything_AlsoNewlyInstalledApps()
    {
        Assert.True(AppPolicy.Default.IsAllowed("steam:730"));
        Assert.True(AppPolicy.Default.IsAllowed("exe:irgendwas-neues"));
        Assert.Equal(0, AppPolicy.Default.Revision);
    }

    [Fact]
    public void Block_ThenAllowAgain_KeepsOnlyRealExceptions()
    {
        var blocked = AppPolicy.Default.WithAllowed(["steam:730", "exe:1"], false, T0);
        Assert.False(blocked.IsAllowed("STEAM:730")); // Groß-/Kleinschreibung egal
        Assert.False(blocked.IsAllowed("exe:1"));
        Assert.True(blocked.IsAllowed("exe:2"));
        Assert.Equal(1, blocked.Revision);
        Assert.Equal(T0, blocked.UpdatedAt);

        var allowed = blocked.WithAllowed(["steam:730"], true, T0.AddMinutes(1));
        Assert.True(allowed.IsAllowed("steam:730"));
        Assert.Equal("exe:1", Assert.Single(allowed.Rules).AppId); // „frei“ ist Standard, braucht keine Regel
        Assert.Equal(2, allowed.Revision);
    }

    [Fact]
    public void CustomApps_AddAndRemove_RemovesTheirRuleToo()
    {
        var app = new CustomApp("custom:abc", "Minecraft", @"C:\Spiele\Minecraft\Minecraft.exe");
        var policy = AppPolicy.Default.WithCustomApp(app, T0).WithAllowed([app.Id], false, T0);
        Assert.Single(policy.CustomApps);
        Assert.False(policy.IsAllowed(app.Id));

        var removed = policy.WithoutCustomApp(app.Id, T0);
        Assert.Empty(removed.CustomApps);
        Assert.Empty(removed.Rules);
        Assert.Equal(3, removed.Revision);
    }

    [Theory]
    [InlineData("Minecraft", @"C:\Spiele\Minecraft\Minecraft.exe", null)]
    [InlineData("Minecraft", "\"C:\\Program Files\\Spiel\\spiel.exe\"", null)] // Anführungszeichen vom Kopieren
    [InlineData("", @"C:\a.exe", "Namen")]
    [InlineData("Spiel", "", "Pfad")]
    [InlineData("Spiel", @"spiel.exe", "vollständig")]
    [InlineData("Spiel", @"C:\Spiele\readme.txt", ".exe")]
    public void CustomApp_Validate(string name, string path, string? errorContains)
    {
        var error = CustomApp.Validate(name, path);
        if (errorContains is null)
            Assert.Null(error);
        else
            Assert.Contains(errorContains, error);
    }

    [Fact]
    public void Json_RoundTrip()
    {
        var policy = AppPolicy.Default.WithAllowed(["exe:1"], false, T0)
            .WithCustomApp(new CustomApp("custom:1", "Tool", @"C:\Tool\tool.exe", "--fast"), T0)
            .WithHidden(["exe:2"], true, T0);

        var json = JsonSerializer.Serialize(policy, MorniLanJsonContext.Default.AppPolicy);
        var back = JsonSerializer.Deserialize(json, MorniLanJsonContext.Default.AppPolicy)!;

        Assert.Equal(policy.Revision, back.Revision);
        Assert.False(back.IsAllowed("exe:1"));
        Assert.Equal("--fast", Assert.Single(back.CustomApps).Arguments);
        Assert.True(back.IsHidden("exe:2"));
    }

    // ───── Ausblenden (Aufräumen) ─────

    [Fact]
    public void Hidden_ToggleAndSurvivesOldJson()
    {
        var policy = AppPolicy.Default.WithHidden(["exe:1", "exe:2"], true, T0);
        Assert.True(policy.IsHidden("EXE:1")); // Groß-/Kleinschreibung egal
        Assert.True(policy.IsHidden("exe:2"));
        Assert.False(policy.IsHidden("exe:3"));
        // Ausblenden sperrt nicht
        Assert.True(policy.IsAllowed("exe:1"));

        var shown = policy.WithHidden(["exe:1"], false, T0);
        Assert.False(shown.IsHidden("exe:1"));
        Assert.True(shown.IsHidden("exe:2"));

        const string old = """{"revision":1,"allowByDefault":true,"rules":[],"customApps":[],"updatedAt":"2026-10-06T05:00:00+00:00"}""";
        var loaded = JsonSerializer.Deserialize(old, MorniLanJsonContext.Default.AppPolicy)!;
        Assert.False(loaded.IsHidden("exe:1"));
    }

    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64)", AppSource.InstalledProgram, null, AppNoise.Runtime)]
    [InlineData(".NET Runtime 8.0", AppSource.InstalledProgram, null, AppNoise.Runtime)]
    [InlineData("Realtek Audio Console", AppSource.InstalledProgram, "Realtek", AppNoise.Driver)]
    [InlineData("NVIDIA Grafiktreiber", AppSource.InstalledProgram, "NVIDIA", AppNoise.Driver)]
    [InlineData("Irgendwas", AppSource.InstalledProgram, "Intel Corporation", AppNoise.Driver)]
    [InlineData("Cortana", AppSource.StoreApp, "Microsoft Corporation", AppNoise.WindowsStore)]
    [InlineData("Discord Updater", AppSource.InstalledProgram, "Discord Inc.", AppNoise.Background)]
    [InlineData("Steam", AppSource.InstalledProgram, "Valve", null)]
    [InlineData("Minecraft", AppSource.Custom, null, null)]
    public void AppNoise_Classifies(string name, AppSource source, string? publisher, string? expected)
    {
        var app = new AppEntry("id:" + name, name, source, Publisher: publisher,
            ExecutablePath: source == AppSource.InstalledProgram ? @"C:\x\y.exe" : null);
        Assert.Equal(expected, AppNoise.Classify(app));
    }

    [Fact]
    public void AppNoise_NeverSuggestsGames()
    {
        // Ein Spiel aus einem Launcher, auch wenn der Name ein Muster enthielte
        var steam = new AppEntry("steam:1", "Driver San Francisco", AppSource.Steam, SteamAppId: 1);
        var epic = new AppEntry("epic:1", "Realtek Racing", AppSource.InstalledProgram, Launcher: GameLaunchers.Epic, Publisher: "Realtek");
        Assert.Null(AppNoise.Classify(steam));
        Assert.Null(AppNoise.Classify(epic));
    }
}
