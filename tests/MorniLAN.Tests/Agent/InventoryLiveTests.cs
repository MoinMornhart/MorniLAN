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
}
