using System.Text.Json;
using System.Text.Json.Serialization;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed record ExecutiveObservation(string Scenario, int EpisodeId, HomeostasisDto Body,
    double AgencyNeed, double Novelty, bool SocialSignalPending, bool Sleeping = false);

/// <summary>Persistent intentions with measured outcomes; no changes to ML state shape or reward.</summary>
public sealed class ExecutivePlanner
{
    private readonly object _gate = new();
    private readonly Action<string> _log;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] Goals = { "regulate", "restore_energy", "explore", "connect", "focus" };
    private PlanningConfig _config = PlanningConfig.Default;
    private PlannerState _state = new();
    private string? _path;
    private long _lastSaveAttemptClock;
    private bool _dirty, _resumePending, _revalidated;
    private string? _lastError;
    private DateTimeOffset? _lastSavedAt;
    private double _appliedBias;
    private string _biasStatus = "idle";
    public ExecutivePlanner(Action<string>? log = null) => _log = log ?? (_ => { });
    public bool Enabled { get { lock (_gate) return _config.Enable; } }
    public long ContextRevision { get { lock (_gate) return _state.Revision; } }

    public void Configure(PlanningConfig settings)
    {
        lock (_gate)
        {
            var config = settings.Normalize();
            var path = System.IO.Path.GetFullPath(config.Path);
            if (_path != path || (_config.Enable && !config.Enable))
            {
                Flush();
                if (_dirty) throw new IOException("Cannot change planning storage before unsaved state is persisted");
            }
            if (config.Enable && _path != path)
            {
                // Parse before replacing state; an unreadable file must never be overwritten.
                var loaded = File.Exists(path)
                    ? JsonSerializer.Deserialize<PlannerState>(File.ReadAllText(path), Json)
                        ?? throw new InvalidDataException("Empty planning state")
                    : new PlannerState();
                Validate(loaded);
                _state = loaded; _path = path; _resumePending = loaded.Active is not null;
                _lastSaveAttemptClock = loaded.AwakeClock; _dirty = false;
                _revalidated = false; _lastError = null;
                _lastSavedAt = File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)) : null;
            }
            _config = config;
        }
    }

    public PlanDto? Begin(IReadOnlyList<GoalDto> goals, ExecutiveObservation observation)
    {
        lock (_gate)
        {
            _appliedBias = 0; _biasStatus = "idle";
            if (!_config.Enable) return null;
            Validate(observation);
            if (_state.Active is { } previous &&
                (previous.Scenario != observation.Scenario || previous.EpisodeId != observation.EpisodeId))
                Finish("interrupted", "context_changed", "Сценарий или эпизод сменился");
            if (observation.Sleeping)
            {
                if (_state.Active is { } sleeping && sleeping.Status != "paused")
                { sleeping.Status = "paused"; sleeping.Evidence = "План приостановлен на время сна"; _dirty = true; Flush(); }
                _biasStatus = "sleeping";
                return Plan();
            }
            _state.AwakeClock++; _dirty = true;
            if (_resumePending)
            {
                _resumePending = false; _revalidated = true; _state.Revision++;
                if (_state.Active is { } resumed) resumed.VerifiedTicks = 0;
            }
            var urgent = observation.Body.Safety < 0.35 || observation.Body.Pain > 0.65;
            if (urgent && _state.Active is { GoalId: not "regulate" })
                Finish("interrupted", "urgent_recovery", "Приоритет безопасности и восстановления");
            var candidates = goals.Where(g => Goals.Contains(g.Id)).GroupBy(g => g.Id)
                .ToDictionary(g => g.Key, g => g.Max(x => x.Urgency), StringComparer.Ordinal);
            if (observation.SocialSignalPending) candidates["connect"] = Math.Max(candidates.GetValueOrDefault("connect"), 0.85);
            if (observation.Body.Energy < 0.30 || observation.Body.Fatigue > 0.75) candidates["restore_energy"] = 0.95;
            if (urgent) candidates["regulate"] = 1.0;
            var top = candidates.Where(g => Needed(g.Key, observation) &&
                    (urgent && g.Key == "regulate" || !_state.Cooldowns.TryGetValue(g.Key, out var until) || until <= _state.AwakeClock))
                .OrderByDescending(g => urgent && g.Key == "regulate" ? 2 : g.Value)
                .ThenBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault();
            if (_state.Active is { } active)
            {
                if (active.Status == "paused") active.VerifiedTicks = 0;
                active.Status = "active"; active.AgeTicks++;
                if (active.AgeTicks >= _config.MaxPlanTicks)
                    Finish("failed", "deadline", "Целевое условие не достигнуто за отведённое время");
                else if (top.Key is not null && top.Key != active.GoalId && active.AgeTicks >= _config.CommitmentTicks &&
                    top.Value > candidates.GetValueOrDefault(active.GoalId, active.InitialUrgency) + _config.SwitchMargin)
                    Finish("interrupted", "higher_priority", "Появилась существенно более срочная цель");
            }
            if (_state.Active is null && top.Key is not null && (urgent && top.Key == "regulate" ||
                !_state.Cooldowns.TryGetValue(top.Key, out var cooldown) || cooldown <= _state.AwakeClock))
            {
                _state.Active = new Intention
                {
                    Id = Guid.NewGuid().ToString("N"), GoalId = top.Key, Scenario = observation.Scenario,
                    EpisodeId = observation.EpisodeId, InitialUrgency = top.Value,
                    BaselineDistance = Math.Max(0.01, Distance(top.Key, observation)),
                    LastProgressClock = _state.AwakeClock, FocusTarget = Math.Max(0.10, observation.AgencyNeed - 0.15)
                };
                _state.Revision++;
                _log("ЦЕЛЬ ПРИНЯТА: " + Title(top.Key));
            }
            MaybeFlush();
            return Plan();
        }
    }

    public void ApplyBias(IDictionary<string, double> legalScores)
    {
        lock (_gate)
        {
            _appliedBias = 0;
            var plan = _state.Active;
            if (!_config.Enable || plan is null || plan.Status == "paused") { _biasStatus = "idle"; return; }
            var preferred = Preferred(plan);
            if (!legalScores.TryGetValue(preferred, out var score) || !double.IsFinite(score))
            { _biasStatus = "unavailable"; return; }
            _appliedBias = _config.MaxActionBias; _biasStatus = "applied";
            legalScores[preferred] = score + _appliedBias;
        }
    }

    public GoalOutcomeDto? Observe(string action, ExecutiveObservation before, ExecutiveObservation after,
        bool invalidAction, int socialSignalsHandled)
    {
        lock (_gate)
        {
            if (!_config.Enable || _state.Active is not { Status: "active" } plan) return null;
            Validate(after);
            if (before.Scenario != plan.Scenario || after.Scenario != plan.Scenario || after.EpisodeId != plan.EpisodeId)
                return Finish("interrupted", "context_changed", "Условия выполнения изменились");
            if (!invalidAction)
            {
                if (action == "explore_signal" && before.Novelty - after.Novelty > 0.00001) plan.Explorations++;
                if (action == "emit_message" && socialSignalsHandled > 0) plan.Contacts += socialSignalsHandled;
            }
            var distance = plan.GoalId == "focus" ? Math.Max(0, after.AgencyNeed - plan.FocusTarget) : Distance(plan.GoalId, after);
            var achieved = plan.GoalId switch
            {
                "explore" => plan.Explorations >= 2 && after.Body.Safety >= 0.5 && after.Body.Pain < 0.4,
                "connect" => plan.Contacts > 0,
                _ => distance <= 1e-9
            };
            plan.Progress = plan.GoalId switch
            {
                "explore" => Math.Min(1, plan.Explorations / 2.0),
                "connect" => plan.Contacts > 0 ? 1 : 0,
                _ => Math.Clamp(1 - distance / plan.BaselineDistance, 0, 1)
            };
            plan.Evidence = Evidence(plan, after);
            if (plan.Progress > plan.BestProgress + 0.01)
            { plan.BestProgress = plan.Progress; plan.LastProgressClock = _state.AwakeClock; }
            plan.VerifiedTicks = achieved && !invalidAction ? plan.VerifiedTicks + 1 : 0;
            plan.Stage = plan.VerifiedTicks > 0 ? "verify" : plan.AgeTicks < 2 ? "prepare" : "execute";
            _dirty = true;
            if (plan.VerifiedTicks >= _config.VerificationTicks)
                return Finish("succeeded", "criterion_met", plan.Evidence);
            if (_state.AwakeClock - plan.LastProgressClock >= _config.StallTicks && plan.VerifiedTicks == 0)
            {
                if (plan.Replans >= _config.MaxReplans)
                    return Finish("failed", "no_progress", plan.Evidence);
                plan.Replans++; plan.LastProgressClock = _state.AwakeClock; plan.Stage = "replan";
                _state.Revision++;
                _log($"ПЛАН ПЕРЕСМОТРЕН: {Title(plan.GoalId)}, попытка {plan.Replans + 1}");
            }
            MaybeFlush();
            return null;
        }
    }

    public ExecutiveStateDto Snapshot()
    {
        lock (_gate) return new(_config.Enable, _state.AwakeClock, Plan(), _state.Succeeded, _state.Failed,
            _state.Interrupted, _state.Outcomes.TakeLast(_config.MaxOutcomes).Reverse().ToArray(), _lastError,
            _lastSavedAt, _state.Active is { } p ? Preferred(p) : null, _appliedBias, _biasStatus, _revalidated);
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty || _path is null) return;
            _lastSaveAttemptClock = _state.AwakeClock;
            string? temp = null;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(file, _state, Json); file.Flush(true); }
                File.Move(temp, _path, overwrite: true);
                _lastSavedAt = DateTimeOffset.UtcNow; _lastError = null; _dirty = false;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message; _log("Ошибка сохранения планирования: " + ex.Message);
                if (temp is not null) try { File.Delete(temp); } catch { }
            }
        }
    }

    private void MaybeFlush() { if (_state.AwakeClock - _lastSaveAttemptClock >= _config.SaveEveryTicks) Flush(); }
    private GoalOutcomeDto Finish(string status, string reason, string evidence)
    {
        var p = _state.Active!;
        if (status == "succeeded") p.Progress = 1;
        var result = new GoalOutcomeDto(p.Id, p.GoalId, Title(p.GoalId), status, reason, evidence,
            p.AgeTicks, p.Replans, p.Progress, DateTimeOffset.UtcNow);
        _state.Outcomes.Add(result);
        if (_state.Outcomes.Count > _config.MaxOutcomes)
            _state.Outcomes.RemoveRange(0, _state.Outcomes.Count - _config.MaxOutcomes);
        if (status == "succeeded") _state.Succeeded++; else if (status == "failed") _state.Failed++; else _state.Interrupted++;
        _state.Cooldowns[p.GoalId] = _state.AwakeClock + _config.GoalCooldownTicks;
        _state.Active = null; _state.Revision++; _dirty = true;
        _log($"РЕЗУЛЬТАТ ЦЕЛИ: {Title(p.GoalId)} / {status}: {evidence}");
        Flush();
        return result;
    }

    private PlanDto? Plan()
    {
        if (!_config.Enable || _state.Active is not { } p) return null;
        var strategy = p.GoalId == "restore_energy" ? "rest" : p.GoalId;
        if (p.Stage == "prepare" && p.GoalId is not ("restore_energy" or "regulate")) strategy = "focus";
        if (p.Replans % 2 == 1) strategy = p.GoalId switch { "restore_energy" => "regulate", "regulate" => "rest", _ => "focus" };
        return new(strategy, Math.Max(0, _config.MaxPlanTicks - p.AgeTicks), p.GoalId, "measured_goal")
        {
            PlanId = p.Id, Status = p.Status, Stage = p.Stage, GoalTitle = Title(p.GoalId),
            SuccessCriterion = Criterion(p.GoalId), Progress = p.Progress, AgeTicks = p.AgeTicks,
            Replans = p.Replans, Evidence = p.Evidence, PreferredAction = Preferred(p)
        };
    }
    private static string Preferred(Intention p) => p.Stage == "prepare" && p.GoalId is not ("restore_energy" or "regulate")
        ? "focus_widen" : (p.GoalId, p.Replans % 2) switch
    {
        ("regulate", 0) => "breathe_slow", ("regulate", _) => "rest_short",
        ("restore_energy", 0) => "rest_short", ("restore_energy", _) => "breathe_slow",
        ("explore", 0) => "explore_signal", ("connect", 0) => "emit_message", ("focus", 0) => "focus_narrow",
        _ => "focus_widen"
    };
    private static bool Needed(string goal, ExecutiveObservation o) => goal switch
    {
        "regulate" or "restore_energy" => Distance(goal, o) > 1e-9,
        "explore" => o.Novelty > 0.05 && o.Body.Safety > 0.5 && o.Body.Energy > 0.35 && o.Body.Pain < 0.4,
        "connect" => o.SocialSignalPending,
        "focus" => o.AgencyNeed > 0.25,
        _ => false
    };
    private static double Distance(string goal, ExecutiveObservation o) => goal switch
    {
        "regulate" => Math.Max(0, 0.70 - o.Body.Safety) + Math.Max(0, o.Body.Pain - 0.20) + Math.Max(0, o.Body.Arousal - 0.35),
        "restore_energy" => Math.Max(0, 0.75 - o.Body.Energy) + Math.Max(0, o.Body.Fatigue - 0.25),
        "focus" => 0.15,
        _ => 1
    };
    private static string Title(string goal) => goal switch
    {
        "regulate" => "Вернуть устойчивое состояние", "restore_energy" => "Восстановить силы",
        "explore" => "Исследовать новизну", "connect" => "Ответить на контакт", "focus" => "Сосредоточиться", _ => goal
    };
    private static string Criterion(string goal) => goal switch
    {
        "regulate" => "Безопасность ≥ 70%, боль ≤ 20%, возбуждение ≤ 35%",
        "restore_energy" => "Энергия ≥ 75%, усталость ≤ 25%",
        "explore" => "Два исследования с наблюдаемым уменьшением новизны при безопасном состоянии",
        "connect" => "Обработан хотя бы один реальный сигнал контакта",
        _ => "Потребность в самостоятельном управлении снижена на 15 процентных пунктов, до уровня не ниже 10%"
    };
    private static string Evidence(Intention p, ExecutiveObservation o) => p.GoalId switch
    {
        "explore" => $"Исследований с измеримым эффектом: {p.Explorations}/2",
        "connect" => $"Обработанных сигналов: {p.Contacts}",
        "restore_energy" => $"Энергия {o.Body.Energy:P0}; усталость {o.Body.Fatigue:P0}",
        "regulate" => $"Безопасность {o.Body.Safety:P0}; боль {o.Body.Pain:P0}; возбуждение {o.Body.Arousal:P0}",
        _ => $"Потребность в управлении {o.AgencyNeed:P0}; целевой уровень ≤ {p.FocusTarget:P0}"
    };
    private static void Validate(ExecutiveObservation o)
    {
        if (string.IsNullOrWhiteSpace(o.Scenario) || new[] { o.Body.Energy, o.Body.Fatigue, o.Body.Safety,
            o.Body.Pain, o.Body.Arousal, o.AgencyNeed, o.Novelty }.Any(v => !double.IsFinite(v) || v < 0 || v > 1))
            throw new InvalidDataException("Invalid planning observation");
    }
    private static void Validate(PlannerState s)
    {
        if (s.SchemaVersion != 1 || s.AwakeClock < 0 || s.Revision < 0 || s.Succeeded < 0 || s.Failed < 0 ||
            s.Interrupted < 0 || s.Cooldowns is null || s.Outcomes is null || s.Outcomes.Count > 200 ||
            s.Cooldowns.Any(p => !Goals.Contains(p.Key) || p.Value < 0) ||
            s.Outcomes.Any(o => o is null || string.IsNullOrWhiteSpace(o.PlanId) || !Goals.Contains(o.GoalId) ||
                o.Status is not ("succeeded" or "failed" or "interrupted") || o.AwakeTicks < 0 || o.Replans < 0 ||
                !double.IsFinite(o.Progress) || o.Progress < 0 || o.Progress > 1))
            throw new InvalidDataException("Invalid persisted planning state");
        if (s.Active is { } p && (!Goals.Contains(p.GoalId) || string.IsNullOrWhiteSpace(p.Id) ||
            string.IsNullOrWhiteSpace(p.Scenario) || p.AgeTicks < 0 || p.Replans < 0 || p.Replans > 5 ||
            p.Status is not ("active" or "paused") || p.Explorations < 0 || p.Contacts < 0 ||
            p.Stage is not ("prepare" or "execute" or "verify" or "replan") || p.VerifiedTicks < 0 || p.VerifiedTicks > 20 ||
            p.LastProgressClock < 0 || p.LastProgressClock > s.AwakeClock ||
            new[] { p.Progress, p.BestProgress, p.InitialUrgency, p.FocusTarget }.Any(v => !double.IsFinite(v) || v < 0 || v > 1) ||
            !double.IsFinite(p.BaselineDistance) || p.BaselineDistance <= 0))
            throw new InvalidDataException("Invalid persisted intention");
    }
    private sealed class PlannerState
    {
        [JsonRequired] public int SchemaVersion { get; set; } = 1;
        [JsonRequired] public long AwakeClock { get; set; }
        [JsonRequired] public long Revision { get; set; }
        [JsonRequired] public long Succeeded { get; set; }
        [JsonRequired] public long Failed { get; set; }
        [JsonRequired] public long Interrupted { get; set; }
        [JsonRequired] public Intention? Active { get; set; }
        [JsonRequired] public Dictionary<string, long> Cooldowns { get; set; } = new();
        [JsonRequired] public List<GoalOutcomeDto> Outcomes { get; set; } = new();
    }
    private sealed class Intention
    {
        [JsonRequired] public string Id { get; set; } = "";
        [JsonRequired] public string GoalId { get; set; } = "";
        [JsonRequired] public string Scenario { get; set; } = "";
        [JsonRequired] public int EpisodeId { get; set; }
        [JsonRequired] public string Status { get; set; } = "active";
        [JsonRequired] public string Stage { get; set; } = "prepare";
        [JsonRequired] public string Evidence { get; set; } = "Ожидается измерение результата";
        [JsonRequired] public double InitialUrgency { get; set; }
        [JsonRequired] public double BaselineDistance { get; set; }
        [JsonRequired] public double FocusTarget { get; set; }
        [JsonRequired] public double Progress { get; set; }
        [JsonRequired] public double BestProgress { get; set; }
        [JsonRequired] public int AgeTicks { get; set; }
        [JsonRequired] public int Replans { get; set; }
        [JsonRequired] public int VerifiedTicks { get; set; }
        [JsonRequired] public int Explorations { get; set; }
        [JsonRequired] public int Contacts { get; set; }
        [JsonRequired] public long LastProgressClock { get; set; }
    }
}
