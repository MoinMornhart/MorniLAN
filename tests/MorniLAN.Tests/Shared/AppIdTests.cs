using MorniLAN.Shared.Models;

namespace MorniLAN.Tests.Shared;

public class AppIdTests
{
    [Fact]
    public void Steam_UsesAppId()
    {
        Assert.Equal("steam:730", AppId.ForSteam(730));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Steam_RejectsInvalidAppId(int appId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AppId.ForSteam(appId));
    }

    [Fact]
    public void Executable_IsStableAcrossCaseAndSlashes()
    {
        var a = AppId.ForExecutable(@"C:\Program Files\Discord\Discord.exe");
        var b = AppId.ForExecutable("c:/program files/discord/DISCORD.EXE");
        Assert.Equal(a, b);
        Assert.StartsWith("exe:", a);
        Assert.Equal("exe:".Length + 16, a.Length);
    }

    [Fact]
    public void Executable_DiffersForDifferentPaths()
    {
        Assert.NotEqual(AppId.ForExecutable(@"C:\a.exe"), AppId.ForExecutable(@"C:\b.exe"));
    }

    [Fact]
    public void SteamEntry_LaunchesViaSteamProtocol()
    {
        var entry = new AppEntry(AppId.ForSteam(570), "Dota 2", AppSource.Steam, SteamAppId: 570);
        Assert.Equal("steam://rungameid/570", entry.LaunchTarget);
    }

    [Fact]
    public void ProgramEntry_LaunchesViaExecutable()
    {
        const string path = @"C:\Games\game.exe";
        var entry = new AppEntry(AppId.ForExecutable(path), "Game", AppSource.Custom, ExecutablePath: path);
        Assert.Equal(path, entry.LaunchTarget);
    }
}
