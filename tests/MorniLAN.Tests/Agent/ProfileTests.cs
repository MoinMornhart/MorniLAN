using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MorniLAN.Admin.Server;
using MorniLAN.Agent.Policy;
using MorniLAN.Shared.Connection;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;
using MorniLAN.Tests.Connection;

namespace MorniLAN.Tests.Agent;

public sealed class ProfileTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    // ───── Freigaben je Profil ─────

    [Fact]
    public void ProfileRule_WinsOverPc_OtherwiseProfileFollowsPc()
    {
        var policy = AppPolicy.Default
            .WithAllowed(["steam:1"], false, T0) // für den PC gesperrt
            .WithAllowedForProfile("profile:a", ["steam:1"], true, T0) // Lena darf trotzdem
            .WithAllowedForProfile("profile:a", ["exe:2"], false, T0); // aber kein Tool

        Assert.True(policy.IsAllowed("steam:1", "profile:a"));
        Assert.False(policy.IsAllowed("exe:2", "profile:a"));
        Assert.False(policy.IsAllowed("steam:1", "profile:b")); // folgt dem PC
        Assert.True(policy.IsAllowed("exe:2", "profile:b"));
        Assert.False(policy.IsAllowed("steam:1", null));
    }

    [Fact]
    public void ProfileRule_EqualToPc_IsDropped_AndProfileCanBeCleared()
    {
        var policy = AppPolicy.Default.WithAllowedForProfile("profile:a", ["exe:1"], false, T0);
        Assert.True(policy.HasProfileRule("exe:1", "profile:a"));

        var back = policy.WithAllowedForProfile("profile:a", ["exe:1"], true, T0);
        Assert.False(back.HasProfileRule("exe:1", "profile:a"));
        Assert.Empty(back.ProfileRules!);

        var cleared = policy.WithoutProfile("profile:a", T0);
        Assert.True(cleared.IsAllowed("exe:1", "profile:a"));
    }

    [Fact]
    public void OldPolicyJson_WithoutNewFields_AllowsProfileCreation()
    {
        const string oldJson = """{"revision":3,"allowByDefault":true,"rules":[],"customApps":[],"updatedAt":"2026-10-06T05:00:00+00:00"}""";
        var policy = JsonSerializer.Deserialize(oldJson, MorniLanJsonContext.Default.AppPolicy)!;

        Assert.False(policy.BlockProfileCreation);
        Assert.Null(policy.ProfileRules);
        Assert.True(policy.IsAllowed("exe:1", "profile:x"));
    }

    // ───── Profile auf dem PC ─────

    // ───── Personalisierung: Passwort, Thema, Farbe ─────

    [Fact]
    public void Password_HashVerify_NeverPlaintext()
    {
        var hash = ProfilePassword.Hash("geheim1");
        Assert.StartsWith("pbkdf2$", hash);
        Assert.DoesNotContain("geheim1", hash);
        Assert.True(ProfilePassword.Verify("geheim1", hash));
        Assert.False(ProfilePassword.Verify("falsch", hash));
        Assert.False(ProfilePassword.Verify("", hash));
        Assert.True(ProfilePassword.LooksLikeHash(hash));
        Assert.True(ProfilePassword.LooksLikeHash(null)); // kein Passwort ist ok
        Assert.False(ProfilePassword.LooksLikeHash("klartext"));
    }

    [Theory]
    [InlineData("abc", "Mindestens")]
    [InlineData("", "Bitte")]
    [InlineData("1234", null)]
    public void Password_Validate(string password, string? errorContains)
    {
        var error = ProfilePassword.Validate(password);
        if (errorContains is null)
            Assert.Null(error);
        else
            Assert.Contains(errorContains, error);
    }

    [Fact]
    public void Themes_NormalizeToKnown()
    {
        Assert.Equal("neon", ProfileThemes.Normalize("neon"));
        Assert.Equal(ProfileThemes.Custom, ProfileThemes.Normalize("custom"));
        Assert.Equal(ProfileThemes.Default, ProfileThemes.Normalize("gibtsnicht"));
        Assert.Equal(ProfileThemes.Default, ProfileThemes.Normalize(null));
    }

    [Fact]
    public void ProfileStore_Edit_AppliesColorThemePassword_AndClear()
    {
        var store = new AgentProfileStore(_dir.Path);
        var profile = store.Add("Lena", "#3DDC84")!;

        Assert.True(store.Edit(new LauncherInbox.ProfileEdit(profile.Id, Color: "#FF6B6B", Theme: "neon",
            PasswordHash: ProfilePassword.Hash("1234"), HasCustomBackground: true, HasAvatar: true)));
        var edited = store.Current.Single();
        Assert.Equal("#FF6B6B", edited.Color);
        Assert.Equal("neon", edited.Theme);
        Assert.True(edited.HasPassword);
        Assert.True(edited.HasCustomBackground);
        Assert.True(edited.HasAvatar);

        // Admin setzt Passwort zurück
        Assert.True(store.ClearPassword(profile.Id));
        Assert.False(new AgentProfileStore(_dir.Path).Current.Single().HasPassword);

        // Kaputter Hash über den Briefkasten wird abgelehnt
        Assert.False(store.Edit(new LauncherInbox.ProfileEdit(profile.Id, PasswordHash: "nicht-pbkdf2")));
        // Unbekanntes Profil
        Assert.False(store.Edit(new LauncherInbox.ProfileEdit("profile:weg", Color: "#FF6B6B")));
    }

    [Fact]
    public void Inbox_AppliesProfileEdit()
    {
        var (inbox, profiles, _, folder) = NewInbox();
        var profile = profiles.Add("Max", "#4C8DFF")!;
        LauncherInbox.WriteProfileEdit(folder, new LauncherInbox.ProfileEdit(profile.Id, Theme: "racing",
            PasswordHash: ProfilePassword.Hash("passwort")));

        inbox.ProcessOnce();

        var edited = profiles.Current.Single();
        Assert.Equal("racing", edited.Theme);
        Assert.True(edited.HasPassword);
    }

    [Fact]
    public void ProfileStore_ValidatesNames_NoDuplicates_Limit_AndPersists()
    {
        var store = new AgentProfileStore(_dir.Path);
        var lena = store.Add("  Lena ", "#3DDC84");
        Assert.NotNull(lena);
        Assert.Equal("Lena", lena.Name);
        Assert.StartsWith("profile:", lena.Id);

        Assert.Null(store.Add("lena", null)); // gleicher Name
        Assert.Null(store.Add("", null));
        Assert.Null(store.Add(new string('x', 30), null));
        Assert.Equal(ProfileColors.All[0], store.Add("Max", "javascript:alert(1)")!.Color); // fremde Farbe → Standard

        for (var i = 0; i < 20; i++)
            store.Add($"Kind {i}", null);
        Assert.Equal(LauncherProfile.MaxProfiles, store.Current.Length);

        var reopened = new AgentProfileStore(_dir.Path);
        Assert.Equal(LauncherProfile.MaxProfiles, reopened.Current.Length);
        Assert.True(reopened.Remove(lena.Id));
        Assert.DoesNotContain(new AgentProfileStore(_dir.Path).Current, p => p.Id == lena.Id);
    }

    // ───── Briefkasten Launcher → Dienst ─────

    private (LauncherInboxService Inbox, AgentProfileStore Profiles, AgentPolicyStore Policy, string Folder) NewInbox(
        Func<DateTimeOffset>? now = null, Action<string>? onAdminHost = null)
    {
        var user = Path.Combine(_dir.Path, "Users", "Freund");
        var folder = LauncherInbox.FolderFor(user);
        Directory.CreateDirectory(folder);
        var data = Path.Combine(_dir.Path, "agent");
        var profiles = new AgentProfileStore(data);
        var policy = new AgentPolicyStore(data);
        var time = now is null ? null : new FakeTime(now);
        var inbox = new LauncherInboxService(profiles, policy, NullLogger<LauncherInboxService>.Instance, () => [user], time,
            onAdminHost: onAdminHost);
        return (inbox, profiles, policy, folder);
    }

    [Fact]
    public void Inbox_ConnectionRequest_TakesAdminHost()
    {
        var hosts = new List<string>();
        var (inbox, _, _, folder) = NewInbox(onAdminHost: hosts.Add);
        LauncherInbox.WriteConnectionRequest(folder, new LauncherInbox.ConnectionRequest("192.168.1.50"));

        inbox.ProcessOnce();

        Assert.Equal("192.168.1.50", Assert.Single(hosts));
    }

    [Fact]
    public void Inbox_ConnectionRequest_IgnoresInvalidHost()
    {
        var hosts = new List<string>();
        var (inbox, _, _, folder) = NewInbox(onAdminHost: hosts.Add);
        LauncherInbox.WriteConnectionRequest(folder, new LauncherInbox.ConnectionRequest("hat leer zeichen"));

        inbox.ProcessOnce();

        Assert.Empty(hosts);
    }

    [Theory]
    [InlineData("192.168.1.50", null)]
    [InlineData("admin-pc", null)]
    [InlineData("admin-pc.tail1234.ts.net", null)]
    [InlineData("", "Adresse")]
    [InlineData("hat leerzeichen auch", "ungültige")]
    [InlineData("has/slash", "ungültige")]
    public void ConnectionRequest_Validate(string host, string? errorContains)
    {
        var error = LauncherInbox.ConnectionRequest.Validate(host);
        if (errorContains is null)
            Assert.Null(error);
        else
            Assert.Contains(errorContains, error);
    }

    [Fact]
    public void Inbox_CreatesProfile_ButDoesNotDeleteUserFiles()
    {
        var (inbox, profiles, _, folder) = NewInbox();
        LauncherInbox.WriteProfileRequest(folder, new LauncherInbox.ProfileRequest("Lena", "#FF6B6B"));

        inbox.ProcessOnce();
        inbox.ProcessOnce(); // zweites Mal: dieselbe Datei nicht erneut

        var profile = Assert.Single(profiles.Current);
        Assert.Equal(("Lena", "#FF6B6B"), (profile.Name, profile.Color));
        Assert.Single(Directory.GetFiles(folder)); // aufräumen tut der Launcher, nicht SYSTEM
    }

    [Fact]
    public void Inbox_RespectsBlockedProfileCreation()
    {
        var (inbox, profiles, policy, folder) = NewInbox();
        policy.Apply(AppPolicy.Default.WithProfileCreationBlocked(true, T0));
        LauncherInbox.WriteProfileRequest(folder, new LauncherInbox.ProfileRequest("Umgehung", "#FF6B6B"));

        inbox.ProcessOnce();

        Assert.Empty(profiles.Current);
    }

    [Fact]
    public void Inbox_HelpRequest_IsRaisedOncePerMinute_AndOldFilesAreIgnored()
    {
        var now = DateTimeOffset.UtcNow;
        var (inbox, _, _, folder) = NewInbox(() => now);
        var raised = new List<HelpRequest>();
        inbox.HelpRequested += raised.Add;

        // Alte Datei (z. B. vom letzten Herbst): zählt nicht
        LauncherInbox.WriteHelpRequest(folder, new LauncherInbox.HelpMessage("Alt"));
        foreach (var file in Directory.GetFiles(folder))
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));
        inbox.ProcessOnce();
        Assert.Empty(raised);

        LauncherInbox.WriteHelpRequest(folder, new LauncherInbox.HelpMessage("Lena"));
        LauncherInbox.WriteHelpRequest(folder, new LauncherInbox.HelpMessage("Lena")); // doppelt gedrückt
        inbox.ProcessOnce();
        var request = Assert.Single(raised);
        Assert.Equal("Lena", request.ProfileName);
        Assert.False(inbox.Help!.Delivered);
        Assert.Equal(request, inbox.PendingHelp);

        inbox.MarkDelivered(request.Id);
        Assert.True(inbox.Help!.Delivered);
        Assert.Null(inbox.PendingHelp);
    }

    [Fact]
    public void Inbox_IgnoresJunctions_SoSystemCannotBeRedirected()
    {
        var user = Path.Combine(_dir.Path, "Users", "Angreifer");
        var target = Path.Combine(_dir.Path, "Systemordner");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "profile-x.json"), """{"name":"Eingeschleust","color":"#FF6B6B"}""");
        var launcherFolder = Path.GetDirectoryName(LauncherInbox.FolderFor(user))!;
        Directory.CreateDirectory(launcherFolder);
        // Briefkasten als Junction auf einen fremden Ordner (geht ohne Admin-Rechte)
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{LauncherInbox.FolderFor(user)}\" \"{target}\"")
                   { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
            mklink.WaitForExit();
        Assert.True(new DirectoryInfo(LauncherInbox.FolderFor(user)).Attributes.HasFlag(FileAttributes.ReparsePoint));

        var data = Path.Combine(_dir.Path, "agent2");
        var profiles = new AgentProfileStore(data);
        new LauncherInboxService(profiles, new AgentPolicyStore(data), NullLogger<LauncherInboxService>.Instance, () => [user])
            .ProcessOnce();

        Assert.Empty(profiles.Current);
        Assert.True(File.Exists(Path.Combine(target, "profile-x.json"))); // nichts gelesen, nichts gelöscht
        Directory.Delete(LauncherInbox.FolderFor(user)); // nur die Junction entfernen
    }

    // ───── Kacheln je Profil ─────

    private static readonly AppEntry[] Apps =
    [
        new("steam:1", "Fortnite", AppSource.Steam, SteamAppId: 1),
        new("steam:2", "GTA", AppSource.Steam, SteamAppId: 2),
        new("exe:3", "Discord", AppSource.InstalledProgram, ExecutablePath: @"C:\D.exe"),
    ];

    private static readonly LauncherProfile Lena = new("profile:lena", "Lena", "#3DDC84", T0);
    private static readonly LauncherProfile Max = new("profile:max", "Max", "#4C8DFF", T0);

    [Fact]
    public void Catalog_WithProfiles_MarksWhoCannotSeeWhat()
    {
        var policy = AppPolicy.Default
            .WithAllowedForProfile(Max.Id, ["steam:2"], false, T0) // Max: kein GTA
            .WithAllowed(["exe:3"], false, T0); // Discord für alle gesperrt

        var list = LauncherCatalog.Build(Apps, policy, _ => null, [Lena, Max]);

        Assert.Equal(["Fortnite", "GTA"], list.Apps.Select(a => a.Name)); // Discord für niemanden frei
        var gta = list.Apps.Single(a => a.Name == "GTA");
        Assert.True(gta.IsVisibleFor(Lena.Id));
        Assert.False(gta.IsVisibleFor(Max.Id));
        Assert.Equal(2, list.Profiles!.Length);
    }

    [Fact]
    public void Catalog_Hash_ChangesWhenProfilePersonalizationChanges()
    {
        var plain = LauncherCatalog.Build(Apps, AppPolicy.Default, _ => null, [Lena]);
        var themed = LauncherCatalog.Build(Apps, AppPolicy.Default, _ => null, [Lena with { Theme = "neon" }]);
        var locked = LauncherCatalog.Build(Apps, AppPolicy.Default, _ => null, [Lena with { PasswordHash = "pbkdf2$a$b" }]);

        Assert.NotEqual(plain.Hash, themed.Hash);
        Assert.NotEqual(plain.Hash, locked.Hash);
    }

    [Fact]
    public void Catalog_WithoutProfiles_UsesNoProfileMarker()
    {
        var list = LauncherCatalog.Build(Apps, AppPolicy.Default.WithAllowed(["exe:3"], false, T0), _ => null);
        Assert.All(list.Apps, a => Assert.True(a.IsVisibleFor(null)));
        Assert.DoesNotContain(list.Apps, a => a.Name == "Discord");

        var blockedCreation = LauncherCatalog.Build(Apps, AppPolicy.Default.WithProfileCreationBlocked(true, T0), _ => null);
        Assert.True(blockedCreation.ProfileCreationBlocked);
        Assert.NotEqual(list.Hash, blockedCreation.Hash);
    }

    // ───── Panel ─────

    [Fact]
    public void AdminProfileStore_KeepsOnlyPlausibleReports()
    {
        var store = new DeviceProfileStore(_dir.Path);
        var device = Guid.NewGuid();
        store.Save(device,
        [
            Lena,
            new LauncherProfile("kein-praefix", "X", "#3DDC84", T0),
            new LauncherProfile("profile:leer", "  ", "#3DDC84", T0),
            new LauncherProfile("profile:farbe", "Farbe", "url(evil)", T0),
        ]);

        var saved = new DeviceProfileStore(_dir.Path).Get(device);
        Assert.Equal(["Lena", "Farbe"], saved.Select(p => p.Name));
        Assert.Equal(ProfileColors.All[0], saved[1].Color);
    }

    [Fact]
    public void HelpInbox_IgnoresDuplicates_AndDismisses()
    {
        var inbox = new HelpInbox();
        var request = new HelpRequest("r1", "Lena", T0);
        inbox.Add(new HelpNotice(Guid.NewGuid(), "MORNI", request, T0));
        inbox.Add(new HelpNotice(Guid.NewGuid(), "MORNI", request, T0)); // nach Wiederverbinden noch einmal

        Assert.Single(inbox.Open);
        inbox.Dismiss("r1");
        Assert.Empty(inbox.Open);
    }

    private sealed class FakeTime(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }
}
