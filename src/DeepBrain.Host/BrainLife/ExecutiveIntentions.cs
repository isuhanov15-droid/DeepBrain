using System.Text.Json.Serialization;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed partial class ExecutivePlanner
{
    private bool _intentionsChanged;
    // Ordered ancestors: root first, nearest parent last. A goal can occur only once.
    // The active leaf may be absent between its completion and the next observation.
    private sealed class IntentionMemory
    {
        [JsonRequired] public int SchemaVersion { get; set; } = 1;
        [JsonRequired] public long Suspensions { get; set; }
        [JsonRequired] public long Resumptions { get; set; }
        [JsonRequired] public long Cancellations { get; set; }
        [JsonRequired] public List<DeferredIntention> Deferred { get; set; } = new();
        [JsonRequired] public List<IntentionTransitionDto> History { get; set; } = new();
    }
    private sealed class DeferredIntention
    {
        [JsonRequired] public Intention Plan { get; set; } = null!;
        [JsonRequired] public string Reason { get; set; } = "";
        [JsonRequired] public long PausedAt { get; set; }
    }
    private IEnumerable<Intention> PendingIntentions() =>
        (_state.Intentions?.Deferred.Select(d => d.Plan) ?? Enumerable.Empty<Intention>())
            .Concat(_state.Active is { } p ? new[] { p } : Array.Empty<Intention>());
    private bool IsDeferred(string goal) => _config.EnableIntentions &&
        _state.Intentions?.Deferred.Any(d => d.Plan.GoalId == goal) == true;
    private void FlushIntentions() { Flush(); _intentionsChanged = false; }
    private void StartIntention(string goal, double urgency, ExecutiveObservation o)
    {
        _state.Active = new Intention
        {
            Id = Guid.NewGuid().ToString("N"), GoalId = goal, Scenario = o.Scenario, EpisodeId = o.EpisodeId,
            InitialUrgency = urgency, BaselineDistance = Math.Max(0.01, Distance(goal, o)),
            LastProgressClock = _state.AwakeClock, FocusTarget = Math.Max(0.10, o.AgencyNeed - 0.15)
        };
        _state.Revision++; _dirty = true;
        _log("ЦЕЛЬ ПРИНЯТА: " + Title(goal));
    }
    private void Transition(Intention p, string kind, string reason, string? related, string evidence)
    {
        var memory = _state.Intentions ??= new();
        if (kind == "suspended") memory.Suspensions++;
        else if (kind == "resumed") memory.Resumptions++;
        else memory.Cancellations++;
        memory.History.Add(new(++_state.Revision, p.Id, p.GoalId, kind, reason, related, evidence, DateTimeOffset.UtcNow));
        if (memory.History.Count > 40) memory.History.RemoveAt(0);
        _dirty = _intentionsChanged = true;
        _log($"НАМЕРЕНИЕ: {Title(p.GoalId)} / {kind}: {evidence}");
    }
    private void Preempt(string goal, string reason, string evidence, ExecutiveObservation o, double urgency = 1)
    {
        if (!_config.EnableIntentions) { Finish("interrupted", reason, evidence); return; }
        var memory = _state.Intentions ??= new();
        // Urgent safety may already be an ancestor. Unwind rather than create a cycle.
        var ancestor = memory.Deferred.FindIndex(d => d.Plan.GoalId == goal);
        if (ancestor >= 0)
        {
            Finish("interrupted", "urgent_ancestor", "Возврат к уже сохранённой задаче восстановления");
            while (memory.Deferred.Count - 1 > ancestor)
                CancelDeferred(memory.Deferred.Count - 1, "urgent_ancestor", "Более срочное намерение требует возврата");
            RestoreDeferred(ancestor);
            return;
        }
        while (memory.Deferred.Count >= _config.MaxDeferredIntentions)
            CancelDeferred(0, "capacity_limit", "Достигнут предел одновременно отложенных намерений");
        var p = _state.Active!;
        p.Status = "deferred"; p.VerifiedTicks = 0;
        if (p.Attempt is { } attempt) attempt.LastEnergy = null;
        memory.Deferred.Add(new() { Plan = p, Reason = reason, PausedAt = _state.AwakeClock });
        _state.Active = null;
        Transition(p, "suspended", reason, goal, evidence);
        StartIntention(goal, urgency, o);
    }
    private void RebaseDeferred(Intention p)
    {
        var deferred = _state.Intentions?.Deferred.FirstOrDefault(d => ReferenceEquals(d.Plan, p));
        if (deferred is null) return;
        var elapsed = _state.AwakeClock - deferred.PausedAt;
        p.LastProgressClock += elapsed;
        if (p.Attempt is { } a) { a.Started += elapsed; a.LastEnergy = null; }
        // Callers removing an item keep its original waiting age in the history evidence.
        deferred.PausedAt = _state.AwakeClock;
    }
    private void RestoreDeferred(int index)
    {
        var memory = _state.Intentions!;
        var p = memory.Deferred[index].Plan;
        RebaseDeferred(p); memory.Deferred.RemoveAt(index);
        p.Status = "active"; p.VerifiedTicks = 0;
        p.Evidence = "Возврат к сохранённому намерению; результат проверяется заново";
        _state.Active = p;
        Transition(p, "resumed", "conditions_ready", null, p.Evidence);
    }
    private void CancelDeferred(int index, string reason, string evidence)
    {
        var memory = _state.Intentions!; var p = memory.Deferred[index].Plan;
        RebaseDeferred(p); memory.Deferred.RemoveAt(index);
        RecordOutcome(p, "interrupted", reason, evidence);
        Transition(p, "cancelled", reason, null, evidence);
    }
    private static string? ObsoleteReason(Intention p, ExecutiveObservation o)
    {
        if (!string.Equals(p.Scenario, o.Scenario, StringComparison.OrdinalIgnoreCase) || p.EpisodeId != o.EpisodeId)
            return "context_changed";
        return p.GoalId switch
        {
            "explore" when o.Novelty <= .05 && p.Explorations < 2 => "novelty_gone",
            "connect" when !o.SocialSignalPending && p.Contacts == 0 => "contact_gone",
            "focus" when o.AgencyNeed <= p.FocusTarget => "resolved_while_deferred",
            "regulate" or "restore_energy" when Distance(p.GoalId, o) <= 1e-9 => "resolved_while_deferred",
            _ => null
        };
    }
    private static string CancellationEvidence(string reason) => reason switch
    {
        "context_changed" => "Сценарий или эпизод сменился; прежний план больше не относится к текущим условиям",
        "novelty_gone" => "Сигнал новизны исчез до возвращения к исследованию",
        "contact_gone" => "Сигнал контакта больше не ожидает ответа",
        "resolved_while_deferred" => "Потребность уже удовлетворена во время другой задачи",
        "deferred_expired" => "Намерение слишком долго ожидало возврата",
        _ => "Условия для продолжения не восстановлены; нужен новый план"
    };
    private void PruneDeferred(ExecutiveObservation o)
    {
        if (!_config.EnableIntentions || _state.Intentions is not { } memory) return;
        for (var i = memory.Deferred.Count - 1; i >= 0; i--)
        {
            var d = memory.Deferred[i];
            // During sleep only context changes invalidate a parent; new observations are needed on waking.
            var reason = o.Sleeping
                ? (!string.Equals(d.Plan.Scenario, o.Scenario, StringComparison.OrdinalIgnoreCase) || d.Plan.EpisodeId != o.EpisodeId ? "context_changed" : null)
                : ObsoleteReason(d.Plan, o) ?? (_state.AwakeClock - d.PausedAt >= _config.MaxDeferredTicks ? "deferred_expired" : null);
            if (reason is not null) CancelDeferred(i, reason, CancellationEvidence(reason));
        }
        while (memory.Deferred.Count > _config.MaxDeferredIntentions)
            CancelDeferred(0, "capacity_limit", "Уменьшен предел отложенных намерений");
    }
    private static string? Prerequisite(Intention p, ExecutiveObservation o)
    {
        if (p.GoalId != "regulate" && (o.Body.Safety < .5 || o.Body.Pain >= .4)) return "regulate";
        if (p.GoalId is not ("regulate" or "restore_energy") && (o.Body.Energy <= .35 || o.Body.Fatigue > .75)) return "restore_energy";
        return null;
    }
    private static bool RecoveryMustContinue(Intention p, ExecutiveObservation o) =>
        p.GoalId == "regulate" && (o.Body.Safety < .5 || o.Body.Pain >= .4) ||
        p.GoalId == "restore_energy" && (o.Body.Energy <= .35 || o.Body.Fatigue > .75);
    private void ResumeDeferred(ExecutiveObservation o)
    {
        if (_state.Intentions is not { } memory) return;
        while (memory.Deferred.Count > 0)
        {
            var index = memory.Deferred.Count - 1; var p = memory.Deferred[index].Plan;
            var obsolete = ObsoleteReason(p, o);
            if (obsolete is not null) { CancelDeferred(index, obsolete, CancellationEvidence(obsolete)); continue; }
            if (Prerequisite(p, o) is { } prerequisite)
            {
                var urgent = prerequisite == "regulate" && (o.Body.Safety < .35 || o.Body.Pain > .65);
                if (!IsDeferred(prerequisite) && (urgent || !_state.Cooldowns.TryGetValue(prerequisite, out var until) || until <= _state.AwakeClock))
                { StartIntention(prerequisite, 1, o); _intentionsChanged = true; return; }
                CancelDeferred(index, "prerequisite_unmet", CancellationEvidence("prerequisite_unmet"));
                continue;
            }
            RestoreDeferred(index); return;
        }
    }
    private IntentionsDto IntentionsSnapshot()
    {
        var memory = _state.Intentions; var deferred = memory?.Deferred ?? new();
        var main = deferred.FirstOrDefault()?.Plan ?? _state.Active;
        return new(_config.Enable && _config.EnableIntentions, main?.Id, main is null ? null : Title(main.GoalId),
            _state.Active is not null ? deferred.LastOrDefault()?.Plan.Id : null,
            memory?.Suspensions ?? 0, memory?.Resumptions ?? 0, memory?.Cancellations ?? 0,
            deferred.Select((d, i) =>
            {
                var child = i + 1 < deferred.Count ? deferred[i + 1].Plan.GoalId : _state.Active?.GoalId;
                return new DeferredIntentionDto(d.Plan.Id, d.Plan.GoalId, Title(d.Plan.GoalId), d.Plan.Progress,
                    d.Plan.AgeTicks, d.Reason, child,
                    (child is null ? "Перед возвратом" : "После «" + Title(child) + "»") +
                    ": проверить актуальность, безопасность и запас сил", _state.AwakeClock - d.PausedAt);
            }).ToArray(), memory?.History.AsEnumerable().Reverse().ToArray() ?? Array.Empty<IntentionTransitionDto>());
    }
    private static void ValidateIntentionMemory(PlannerState s)
    {
        if (s.Intentions is not { } memory) return;
        if (memory.SchemaVersion != 1 || memory.Suspensions < 0 || memory.Resumptions < 0 || memory.Cancellations < 0 ||
            memory.Resumptions + memory.Cancellations > memory.Suspensions ||
            memory.Deferred is null || memory.History is null || memory.Deferred.Count > 4 || memory.History.Count > 40 ||
            memory.Suspensions - memory.Resumptions - memory.Cancellations != memory.Deferred.Count ||
            memory.Deferred.Any(d => d is null || d.Plan is null || d.PausedAt < 0 || d.PausedAt > s.AwakeClock ||
                d.Plan.LastProgressClock > d.PausedAt || d.Plan.Attempt is { } a && a.Started > d.PausedAt ||
                d.Reason is not ("urgent_recovery" or "energy_recovery" or "higher_priority")) ||
            memory.History.Any(h => h is null || h.Revision < 0 || h.Revision > s.Revision ||
                string.IsNullOrWhiteSpace(h.PlanId) || !Goals.Contains(h.GoalId) ||
                h.Kind is not ("suspended" or "resumed" or "cancelled") || string.IsNullOrWhiteSpace(h.Reason) ||
                string.IsNullOrWhiteSpace(h.Evidence) || h.RelatedGoalId is not null && !Goals.Contains(h.RelatedGoalId)) ||
            memory.History.Select(h => h.Revision).Distinct().Count() != memory.History.Count)
            throw new InvalidDataException("Invalid persisted intention hierarchy");
    }
}
