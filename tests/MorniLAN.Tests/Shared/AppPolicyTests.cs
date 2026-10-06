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
            .WithCustomApp(new CustomApp("custom:1", "Tool", @"C:\Tool\tool.exe", "--fast"), T0);

        var json = JsonSerializer.Serialize(policy, MorniLanJsonContext.Default.AppPolicy);
        var back = JsonSerializer.Deserialize(json, MorniLanJsonContext.Default.AppPolicy)!;

        Assert.Equal(policy.Revision, back.Revision);
        Assert.False(back.IsAllowed("exe:1"));
        Assert.Equal("--fast", Assert.Single(back.CustomApps).Arguments);
    }
}
