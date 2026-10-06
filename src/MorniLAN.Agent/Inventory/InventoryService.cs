using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MorniLAN.Shared.Models;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Agent.Inventory;

/// <summary>Hält die aktuelle Programmliste samt Bildern bereit. Es läuft immer nur ein Einlesen gleichzeitig.</summary>
/// <param name="covers">Cover aus dem Steam-Shop. Ohne eigene Sammelfunktion (also im echten Betrieb) automatisch an.</param>
internal sealed class InventoryService(
    ILogger<InventoryService> logger,
    Func<InventoryCollector.Result>? collect = null,
    StoreCoverService? covers = null)
{
    private readonly ImageService _images = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<InventoryCollector.Result> _collect = collect ?? InventoryCollector.Collect;
    private readonly StoreCoverService? _covers =
        covers ?? (collect is null ? new StoreCoverService(Path.Combine(AgentPaths.Data, "covers")) : null);
    private InventoryReport? _current;

    public InventoryReport? Current => Volatile.Read(ref _current);

    public AppImage? GetImage(string hash) => _images.Get(hash);

    /// <summary>Aktuelle Liste, höchstens <paramref name="maxAge"/> alt, sonst neu eingelesen.</summary>
    public async Task<InventoryReport> GetAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        if (Current is { } current && DateTimeOffset.UtcNow - current.CollectedAt < maxAge)
            return current;
        return await RefreshAsync(cancellationToken);
    }

    public async Task<InventoryReport> RefreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var watch = Stopwatch.StartNew();
            var report = await Task.Run(async () =>
            {
                var result = _collect();
                foreach (var problem in result.Problems)
                    logger.LogWarning("Programmliste: {Problem}", problem);
                var items = result.Items;
                if (_covers is not null)
                {
                    try { items = await _covers.FillAsync(items, cancellationToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Programmliste: Cover aus dem Steam-Shop nicht geladen");
                    }
                }
                var apps = items.Select(_images.WithImages).ToArray();
                return new InventoryReport(DateTimeOffset.UtcNow, apps, ContentHash(apps));
            }, cancellationToken);
            logger.LogInformation(
                "Programmliste: {Count} Einträge, davon {Visible} sichtbar ({Steam} Steam-Spiele, {Other} aus anderen Launchern), {Ms} ms",
                report.Apps.Length, report.Apps.Count(a => !a.IsSystemComponent),
                report.Apps.Count(a => a.Source == AppSource.Steam), report.Apps.Count(a => a.Launcher is not null),
                watch.ElapsedMilliseconds);
            Volatile.Write(ref _current, report);
            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Gleich, solange sich keine Einträge ändern (die Uhrzeit des Einlesens zählt nicht mit).</summary>
    internal static string ContentHash(AppEntry[] apps) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(apps, MorniLanJsonContext.Default.AppEntryArray)));
}
