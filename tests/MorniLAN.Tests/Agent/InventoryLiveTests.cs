using MorniLAN.Agent.Inventory;

namespace MorniLAN.Tests.Agent;

/// <summary>
/// Läuft gegen den echten PC. Nur auf Wunsch: MorniLAN.Tests.exe -explicit only -method "*Live*".
/// Zeigt, was der Agent auf diesem Rechner finden würde.
/// </summary>
public class InventoryLiveTests
{
    [Fact(Explicit = true)]
    public void Live_CollectOnThisPc()
    {
        var result = InventoryCollector.Collect();
        var output = TestContext.Current.TestOutputHelper!;
        foreach (var problem in result.Problems)
            output.WriteLine("PROBLEM " + problem);
        foreach (var group in result.Items.GroupBy(i => (i.App.Source, i.App.IsSystemComponent)).OrderBy(g => g.Key.Source))
            output.WriteLine($"{group.Key.Source} {(group.Key.IsSystemComponent ? "(System)" : "")}: {group.Count()}");
        foreach (var item in result.Items.Where(i => !i.App.IsSystemComponent))
            output.WriteLine($"  {item.App.Source,-16} {item.App.Name} | {item.App.LaunchTarget} | Bild: {item.Images.CoverFile ?? item.Images.IconFile ?? item.Images.IconFromExecutable ?? "-"}");
        output.WriteLine("--- als System ausgeblendet:");
        foreach (var item in result.Items.Where(i => i.App.IsSystemComponent))
            output.WriteLine($"  {item.App.Name}");
        Assert.NotEmpty(result.Items);
    }

    /// <summary>Holt echte Cover aus dem Steam-Shop (braucht Internet).</summary>
    [Fact(Explicit = true)]
    public async Task Live_StoreCovers()
    {
        var dir = Directory.CreateTempSubdirectory("mornilan-live-covers");
        try
        {
            var service = new StoreCoverService(dir.FullName);
            InventoryItem Game(string name, string launcher) => new(
                new MorniLAN.Shared.Models.AppEntry("x:" + name, name, MorniLAN.Shared.Models.AppSource.InstalledProgram,
                    Launcher: launcher), new ImageSources());

            var items = await service.FillAsync(
            [
                Game("Assassin’s Creed® Valhalla", MorniLAN.Shared.Models.GameLaunchers.Ubisoft), // Hochformat
                Game("Battlefield™ 6", MorniLAN.Shared.Models.GameLaunchers.Ea), // nur Platzhalter → Querformat
                Game("Fortnite", MorniLAN.Shared.Models.GameLaunchers.Epic), // gibt es auf Steam nicht
            ], TestContext.Current.CancellationToken);

            foreach (var item in items)
                TestContext.Current.TestOutputHelper!.WriteLine(
                    $"{item.App.Name}: {(item.Images.CoverFile is { } f ? $"{new FileInfo(f).Length:N0} Bytes" : "kein Cover")}");
            Assert.True(new FileInfo(items[0].Images.CoverFile!).Length > 20_000);
            Assert.True(new FileInfo(items[1].Images.CoverFile!).Length > 20_000);
            Assert.Null(items[2].Images.CoverFile);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Füllt ein Admin-Datenverzeichnis mit der echten Liste dieses PCs (als Gerät „Vorschau“), um die Seite
    /// „Freigaben“ ohne zweiten PC anzusehen. Ziel: Umgebungsvariable MORNILAN_PREVIEW_DIR.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Live_FillAdminDataForPreview()
    {
        var target = Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_DIR")
                     ?? throw new InvalidOperationException("MORNILAN_PREVIEW_DIR setzen");
        // MORNILAN_PREVIEW_SAMPLES=1: zusätzlich Beispiel-Spiele aus den anderen Launchern (gibt es auf diesem PC nicht)
        var samples = Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_SAMPLES") == "1";
        var service = samples
            ? new InventoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryService>.Instance,
                () =>
                {
                    var real = InventoryCollector.Collect();
                    return real with { Items = [.. real.Items, .. SampleLauncherGames()] };
                },
                new StoreCoverService(Path.Combine(target, "preview-covers")))
            : new InventoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryService>.Instance);
        var report = await service.RefreshAsync(TestContext.Current.CancellationToken);

        var store = new MorniLAN.Admin.Server.InventoryStore(target);
        var deviceId = Guid.Parse("00000000-0000-0000-0000-00000000beef");
        new MorniLAN.Admin.Server.DeviceRegistry(target).Add(
            new MorniLAN.Admin.Server.PairedDevice(deviceId, Environment.MachineName + " (Vorschau)", new string('0', 64), DateTimeOffset.UtcNow));
        store.Save(deviceId, report);
        var saved = 0;
        foreach (var hash in store.MissingImages(report))
            if (service.GetImage(hash) is { } image && store.TrySaveImage(image))
                saved++;
        TestContext.Current.TestOutputHelper!.WriteLine($"{report.Apps.Length} Einträge, {saved} Bilder nach {target}");
    }

    private static IEnumerable<InventoryItem> SampleLauncherGames()
    {
        (string Name, string Launcher, string Prefix)[] games =
        [
            ("Fortnite", MorniLAN.Shared.Models.GameLaunchers.Epic, "epic"),
            ("Hogwarts Legacy", MorniLAN.Shared.Models.GameLaunchers.Epic, "epic"),
            ("The Witcher 3: Wild Hunt", MorniLAN.Shared.Models.GameLaunchers.Gog, "gog"),
            ("Assassin’s Creed® Valhalla", MorniLAN.Shared.Models.GameLaunchers.Ubisoft, "ubisoft"),
            ("Battlefield™ 6", MorniLAN.Shared.Models.GameLaunchers.Ea, "ea"),
            ("Overwatch® 2", MorniLAN.Shared.Models.GameLaunchers.BattleNet, "bnet"),
        ];
        return games.Select(g => new InventoryItem(
            new MorniLAN.Shared.Models.AppEntry(MorniLAN.Shared.Models.AppId.ForLauncher(g.Prefix, g.Name), g.Name,
                MorniLAN.Shared.Models.AppSource.InstalledProgram, Publisher: "Beispiel für die Vorschau", Launcher: g.Launcher),
            new ImageSources()));
    }
}
