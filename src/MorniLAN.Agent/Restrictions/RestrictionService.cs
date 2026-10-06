using System.Diagnostics;
using MorniLAN.Agent.Policy;
using MorniLAN.Shared.Models;

namespace MorniLAN.Agent.Restrictions;

/// <summary>
/// Setzt die Sperren durch: schreibt die Benutzer-Richtlinien der eingeschränkten Konten und beendet
/// (Prozess-Wächter) gesperrte Programme, die trotzdem starten. Administratorkonten bleiben immer unangetastet.
/// App Control kommt als eigener Schritt dazu.
/// </summary>
/// <param name="writePolicies">Schreibt/entfernt die Registry-Richtlinien eines Kontos (injizierbar für Tests).</param>
/// <param name="terminate">Beendet einen Prozess (injizierbar für Tests).</param>
/// <param name="enumerate">Laufende Prozesse (injizierbar für Tests).</param>
internal sealed class RestrictionService(
    AgentPolicyStore policy,
    ILogger<RestrictionService> logger,
    Action<string, IReadOnlySet<string>>? writePolicies = null,
    Action<string>? clearPolicies = null,
    Action<int>? terminate = null,
    Func<IEnumerable<ProcessOwnership.RunningProcess>>? enumerate = null,
    Func<IReadOnlyList<LocalAccount>>? scanAccounts = null,
    Func<bool>? isPaused = null,
    Action? resume = null,
    TimeProvider? time = null) : BackgroundService
{
    private readonly Action<string, IReadOnlySet<string>> _writePolicies = writePolicies ?? UserPolicyWriter.Apply;
    private readonly Action<string> _clearPolicies = clearPolicies ?? UserPolicyWriter.Clear;
    private readonly Action<int> _terminate = terminate ?? TerminateProcess;
    private readonly Func<IEnumerable<ProcessOwnership.RunningProcess>> _enumerate = enumerate ?? ProcessOwnership.Enumerate;
    private readonly Func<IReadOnlyList<LocalAccount>> _scanAccounts = scanAccounts ?? AccountScanner.Scan;
    private readonly Func<bool> _isPaused = isPaused ?? (() => false);
    private readonly Action _resume = resume ?? (() => { });
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _lock = new();

    private IReadOnlyList<LocalAccount> _accounts = [];
    private RestrictionState _state = RestrictionState.Idle;

    /// <summary>Konten des PCs, zuletzt eingelesen.</summary>
    public event Action<IReadOnlyList<LocalAccount>>? AccountsChanged;

    /// <summary>Stand der Sperren hat sich geändert (zum Melden ans Panel).</summary>
    public event Action<RestrictionState>? StateChanged;

    /// <summary>Stand der Sperren (für die Meldung ans Panel).</summary>
    public RestrictionState State
    {
        get
        {
            lock (_lock)
                return _state;
        }
    }

    public IReadOnlyList<LocalAccount> Accounts
    {
        get
        {
            lock (_lock)
                return _accounts;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RescanAccounts();
        policy.Changed += OnPolicyChanged;
        try
        {
            ApplyPolicies();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3), _time);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                GuardProcesses();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            policy.Changed -= OnPolicyChanged;
        }
    }

    private void OnPolicyChanged(AppPolicy _) => ApplyPolicies();

    /// <summary>Admin hat „Sperren jetzt aktivieren“ gedrückt – beendet auch eine Notfall-Entsperrung.</summary>
    public void ApplyNow()
    {
        _resume();
        RescanAccounts();
        ApplyPolicies();
    }

    private void RescanAccounts()
    {
        try
        {
            var accounts = _scanAccounts();
            lock (_lock)
                _accounts = accounts;
            AccountsChanged?.Invoke(accounts);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Konten nicht gelesen: {Error}", ex.Message);
        }
    }

    /// <summary>Richtlinien aller Konten setzen bzw. entfernen, je nach aktueller Freigabe.</summary>
    private void ApplyPolicies()
    {
        var current = policy.Current;
        var paused = _isPaused();
        var blockedAreas = (current.BlockedAreas ?? []).Where(WindowsAreas.IsKnown).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var restricted = 0;

        foreach (var account in Accounts)
        {
            try
            {
                // Bei Notfall-Entsperrung alle Konten freigeben, egal was eingestellt ist
                if (!paused && RestrictionPlan.IsAccountRestricted(current, account.Sid, account.IsAdministrator))
                {
                    _writePolicies(account.Sid, blockedAreas);
                    restricted++;
                }
                else
                {
                    _clearPolicies(account.Sid);
                }
            }
            catch (Exception ex)
            {
                problems.Add($"{account.Name}: {ex.Message}");
            }
        }

        var message = paused
            ? "Notfall-Entsperrung aktiv – alle Sperren aufgehoben, bis der Admin sie wieder einschaltet."
            : problems.Count == 0 ? "" : $"Konnte nicht alle Konten setzen: {string.Join("; ", problems)}";
        RestrictionState state;
        lock (_lock)
        {
            state = _state = new RestrictionState(problems.Count == 0, restricted, "off", message);
        }
        StateChanged?.Invoke(state);
        if (restricted > 0 || current.BlockedAreas is { Length: > 0 })
            logger.LogInformation("Sperren angewandt: {Count} Konten, Bereiche {Areas}, Pause {Paused}", restricted,
                string.Join(", ", blockedAreas), paused);
        GuardProcesses();
    }

    /// <summary>Gesperrte Programme beenden, die in eingeschränkten Konten laufen.</summary>
    private void GuardProcesses()
    {
        if (_isPaused())
            return;
        var current = policy.Current;
        var blocked = RestrictionPlan.BlockedExecutables((current.BlockedAreas ?? []).Where(WindowsAreas.IsKnown));
        if (blocked.Count == 0)
            return;
        var restrictedSids = Accounts
            .Where(a => RestrictionPlan.IsAccountRestricted(current, a.Sid, a.IsAdministrator))
            .Select(a => a.Sid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (restrictedSids.Count == 0)
            return;

        foreach (var process in _enumerate())
        {
            if (process.OwnerSid is { } sid && restrictedSids.Contains(sid)
                && RestrictionPlan.ShouldTerminate(process.ExecutablePath, blocked))
            {
                try
                {
                    _terminate(process.Id);
                    logger.LogInformation("Gesperrtes Programm beendet: {Path} (PID {Id})", process.ExecutablePath, process.Id);
                }
                catch (Exception ex)
                {
                    logger.LogDebug("Konnte {Id} nicht beenden: {Error}", process.Id, ex.Message);
                }
            }
        }
    }

    private static void TerminateProcess(int id)
    {
        using var process = Process.GetProcessById(id);
        process.Kill();
    }
}
