using System.Text.Json;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Tests.Shared;

public class SerializationTests
{
    [Fact]
    public void AdminCommands_RoundTripPolymorphically()
    {
        AdminCommand[] commands =
        [
            new InstallPackageCommand("Valve.Steam"),
            new RestartCommand(30),
            new ShowMessageCommand("Hallo", "Update kommt gleich"),
            new LockNowCommand(),
        ];

        foreach (var command in commands)
        {
            var json = JsonSerializer.Serialize(command, MorniLanJsonContext.Default.AdminCommand);
            var back = JsonSerializer.Deserialize(json, MorniLanJsonContext.Default.AdminCommand);
            Assert.Equal(command, back);
        }
    }

    [Fact]
    public void AdminCommand_UsesReadableTypeDiscriminator()
    {
        var json = JsonSerializer.Serialize<AdminCommand>(new InstallPackageCommand("Discord.Discord"),
            MorniLanJsonContext.Default.AdminCommand);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("install", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("Discord.Discord", doc.RootElement.GetProperty("wingetId").GetString());
    }

    [Fact]
    public void AppEntry_SerializesEnumsAsStrings_AndOmitsNulls()
    {
        var entry = new AppEntry("steam:730", "Counter-Strike 2", AppSource.Steam, SteamAppId: 730);
        var json = JsonSerializer.Serialize(entry, MorniLanJsonContext.Default.AppEntry);

        Assert.Contains("\"source\":\"Steam\"", json);
        Assert.DoesNotContain("executablePath", json);
        Assert.Equal(entry, JsonSerializer.Deserialize(json, MorniLanJsonContext.Default.AppEntry));
    }
}
