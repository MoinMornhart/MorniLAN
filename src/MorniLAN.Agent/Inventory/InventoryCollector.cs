namespace MorniLAN.Agent.Inventory;

/// <summary>Sammelt alles ein. Jede Quelle für sich: Fällt eine aus (z. B. kein Steam), laufen die anderen weiter.</summary>
internal static class InventoryCollector
{
    public sealed record Result(IReadOnlyList<InventoryItem> Items, IReadOnlyList<string> Problems);

    public static Result Collect()
    {
        var problems = new List<string>();

        T Try<T>(string source, Func<T> scan, T fallback)
        {
            try { return scan(); }
            catch (Exception ex)
            {
                problems.Add($"{source}: {ex.Message}");
                return fallback;
            }
        }

        var steam = Try("Steam", () => SteamScanner.FindSteamRoot() is { } root ? SteamScanner.Scan(root) : [],
            (IReadOnlyList<SteamGame>)[]);
        var uninstall = Try("Programmliste", UninstallRegistryScanner.Scan, (IReadOnlyList<UninstallEntry>)[]);
        var shortcuts = Try("Startmenü", StartMenuScanner.Scan, (IReadOnlyList<StartMenuShortcut>)[]);
        var store = Try("Store-Apps", StoreAppScanner.Scan, (IReadOnlyList<StoreApp>)[]);

        return new Result(InventoryBuilder.Build(steam, uninstall, shortcuts, store), problems);
    }
}
