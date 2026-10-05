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

    /// <summary>
    /// Füllt ein Admin-Datenverzeichnis mit der echten Liste dieses PCs (als Gerät „Vorschau“), um die Seite
    /// „Freigaben“ ohne zweiten PC anzusehen. Ziel: Umgebungsvariable MORNILAN_PREVIEW_DIR.
    /// </summary>
    [Fact(Explicit = true)]
    public void Live_FillAdminDataForPreview()
    {
        var target = Environment.GetEnvironmentVariable("MORNILAN_PREVIEW_DIR")
                     ?? throw new InvalidOperationException("MORNILAN_PREVIEW_DIR setzen");
        var service = new InventoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryService>.Instance);
        var report = service.RefreshAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();

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
}
