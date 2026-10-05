namespace MorniLAN.Shared.Models;

/// <summary>Freigabe-Status eines App-Eintrags.</summary>
public sealed record ApprovalRule(string AppId, bool Approved, DateTimeOffset UpdatedAt);

/// <summary>
/// Menge aller Freigaben. Grundregel: Alles, was nicht ausdrücklich freigegeben ist, ist gesperrt.
/// </summary>
public sealed class ApprovalSet
{
    private readonly Dictionary<string, ApprovalRule> _rules = new(StringComparer.OrdinalIgnoreCase);

    public ApprovalSet() { }

    public ApprovalSet(IEnumerable<ApprovalRule> rules)
    {
        foreach (var rule in rules)
            Apply(rule);
    }

    public IReadOnlyCollection<ApprovalRule> Rules => _rules.Values;

    public bool IsApproved(string appId) =>
        _rules.TryGetValue(appId, out var rule) && rule.Approved;

    /// <summary>
    /// Übernimmt eine Regel. Ältere Regeln überschreiben keine neueren
    /// (wichtig, wenn Änderungen verspätet oder doppelt ankommen).
    /// </summary>
    /// <returns>true, wenn sich der gespeicherte Stand geändert hat.</returns>
    public bool Apply(ApprovalRule rule)
    {
        if (_rules.TryGetValue(rule.AppId, out var existing) && existing.UpdatedAt > rule.UpdatedAt)
            return false;
        _rules[rule.AppId] = rule;
        return existing != rule;
    }

    /// <summary>Filtert eine App-Liste auf die freigegebenen Einträge.</summary>
    public IEnumerable<AppEntry> FilterApproved(IEnumerable<AppEntry> apps) =>
        apps.Where(a => IsApproved(a.Id));
}
