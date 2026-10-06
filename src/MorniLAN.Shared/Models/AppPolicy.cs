namespace MorniLAN.Shared.Models;

/// <summary>Ausnahme vom Standard für einen App-Eintrag.</summary>
public sealed record ApprovalRule(string AppId, bool Approved, DateTimeOffset UpdatedAt);

/// <summary>Vom Admin angelegter Eintrag: ein Programm, das der Agent nicht selbst gefunden hat.</summary>
/// <param name="ExecutablePath">Pfad zur EXE auf dem verwalteten PC.</param>
public sealed record CustomApp(string Id, string Name, string ExecutablePath, string? Arguments = null)
{
    public const int MaxNameLength = 80;

    /// <summary>Fehlermeldung für die Eingabe, oder null, wenn alles passt.</summary>
    public static string? Validate(string? name, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Bitte einen Namen eingeben.";
        if (name.Trim().Length > MaxNameLength)
            return $"Der Name ist zu lang (höchstens {MaxNameLength} Zeichen).";
        var path = executablePath?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
            return "Bitte den Pfad zur Programmdatei eingeben.";
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(path))
            return "Der Pfad muss vollständig sein, z. B. C:\\Spiele\\Spiel\\spiel.exe.";
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "Der Pfad muss auf eine .exe-Datei zeigen.";
        return null;
    }
}

/// <summary>
/// Freigaben eines PCs. Das Panel ist die einzige Quelle: Es zählt <see cref="Revision"/> bei jeder Änderung hoch,
/// der Agent übernimmt nur neuere Stände (wichtig, wenn Änderungen verspätet oder doppelt ankommen).
/// </summary>
/// <param name="AllowByDefault">
/// Gilt für alles ohne eigene Regel, auch für neu installierte Programme. Vom Nutzer entschieden (2026-10-06):
/// standardmäßig alles frei, gesperrt wird gezielt.
/// </param>
/// <param name="Rules">Nur Abweichungen vom Standard.</param>
/// <param name="ProfileRules">Abweichungen einzelner Profile von den Regeln des PCs (M5).</param>
/// <param name="BlockProfileCreation">
/// Am PC keine neuen Profile anlegen (sonst könnte man eine Sperre umgehen, indem man sich ein neues Profil macht).
/// Absichtlich so herum benannt: Fehlt das Feld (ältere Daten), ist es false, das Anlegen also erlaubt.
/// </param>
public sealed record AppPolicy(
    long Revision,
    bool AllowByDefault,
    ApprovalRule[] Rules,
    CustomApp[] CustomApps,
    DateTimeOffset UpdatedAt,
    ProfileRuleSet[]? ProfileRules = null,
    bool BlockProfileCreation = false)
{
    /// <summary>Stand, bevor der Admin etwas eingestellt hat.</summary>
    public static AppPolicy Default { get; } = new(0, true, [], [], DateTimeOffset.UnixEpoch);

    /// <summary>Regeln des PCs (ohne Profil).</summary>
    public bool IsAllowed(string appId) => Find(Rules, appId) ?? AllowByDefault;

    /// <summary>Für ein Profil: dessen Regel, sonst die des PCs, sonst der Standard.</summary>
    public bool IsAllowed(string appId, string? profileId) =>
        (profileId is null ? null : Find(RulesOf(profileId), appId)) ?? IsAllowed(appId);

    /// <summary>Hat das Profil für diesen Eintrag eine eigene Regel?</summary>
    public bool HasProfileRule(string appId, string profileId) => Find(RulesOf(profileId), appId) is not null;

    private ApprovalRule[] RulesOf(string profileId) =>
        (ProfileRules ?? []).FirstOrDefault(p => p.ProfileId == profileId)?.Rules ?? [];

    private static bool? Find(ApprovalRule[] rules, string appId)
    {
        foreach (var rule in rules)
        {
            if (string.Equals(rule.AppId, appId, StringComparison.OrdinalIgnoreCase))
                return rule.Approved;
        }
        return null;
    }

    /// <summary>
    /// Für ein Profil freigeben oder sperren. Entspricht das der Regel des PCs, fällt die Profilregel weg
    /// (das Profil folgt dann wieder dem PC).
    /// </summary>
    public AppPolicy WithAllowedForProfile(string profileId, IEnumerable<string> appIds, bool allowed, DateTimeOffset now)
    {
        var ids = appIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rules = RulesOf(profileId).Where(r => !ids.Contains(r.AppId)).ToList();
        rules.AddRange(ids.Where(id => IsAllowed(id) != allowed).Select(id => new ApprovalRule(id, allowed, now)));
        return Next(now) with { ProfileRules = WithProfile(profileId, [.. rules.OrderBy(r => r.AppId, StringComparer.OrdinalIgnoreCase)]) };
    }

    /// <summary>Alle eigenen Regeln eines Profils entfernen (z. B. wenn das Profil gelöscht wurde).</summary>
    public AppPolicy WithoutProfile(string profileId, DateTimeOffset now) =>
        Next(now) with { ProfileRules = WithProfile(profileId, []) };

    public AppPolicy WithProfileCreationBlocked(bool blocked, DateTimeOffset now) =>
        Next(now) with { BlockProfileCreation = blocked };

    private ProfileRuleSet[] WithProfile(string profileId, ApprovalRule[] rules) =>
        [.. (ProfileRules ?? []).Where(p => p.ProfileId != profileId), .. rules.Length > 0 ? [new ProfileRuleSet(profileId, rules)] : Array.Empty<ProfileRuleSet>()];

    /// <summary>Mehrere Einträge auf einmal freigeben oder sperren.</summary>
    public AppPolicy WithAllowed(IEnumerable<string> appIds, bool allowed, DateTimeOffset now)
    {
        var ids = appIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rules = Rules.Where(r => !ids.Contains(r.AppId)).ToList();
        // Regeln, die dem Standard entsprechen, braucht es nicht
        if (allowed != AllowByDefault)
            rules.AddRange(ids.Select(id => new ApprovalRule(id, allowed, now)));
        return Next(now) with { Rules = [.. rules.OrderBy(r => r.AppId, StringComparer.OrdinalIgnoreCase)] };
    }

    public AppPolicy WithCustomApp(CustomApp app, DateTimeOffset now) =>
        Next(now) with { CustomApps = [.. CustomApps.Where(c => c.Id != app.Id), app] };

    /// <summary>Eigenen Eintrag entfernen, samt seiner Regel.</summary>
    public AppPolicy WithoutCustomApp(string id, DateTimeOffset now) =>
        Next(now) with
        {
            CustomApps = [.. CustomApps.Where(c => c.Id != id)],
            Rules = [.. Rules.Where(r => !string.Equals(r.AppId, id, StringComparison.OrdinalIgnoreCase))],
        };

    private AppPolicy Next(DateTimeOffset now) => this with { Revision = Revision + 1, UpdatedAt = now };
}
