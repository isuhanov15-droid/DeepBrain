using System.Linq;
using System.Linq;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Trace;

namespace DeepBrain.Host.BrainLife;

public sealed class LifeLoop
{
    private readonly WorldSim _world;
    private readonly HomeostasisEngine _homeostasis;
    private readonly InstinctEngine _instincts;
    private readonly EmotionEngine _emotion;
    private readonly ActionSelector _selector;
    private readonly Actuator _actuator;
    private readonly RewardEngine _reward;
    private readonly LearningEngine _learning;
    private readonly Action<LifeStateDto, CancellationToken> _broadcast;
    private readonly Action<TraceDto, CancellationToken> _trace;
    private readonly Action<LifeOutputDto, CancellationToken> _output;
    private readonly Action<string> _log;
    private readonly EpisodeMemory _memory = new();
    private readonly LoopDetector _loop = new();
    private readonly MoodInertiaEngine _inertia = new();
    private readonly DominantDriveResolver _driveResolver = new();
    private readonly CircadianClock _clock = new();
    private readonly SleepEngine _sleep = new();
    private readonly GoalResolver _goals = new();
    private readonly PlanEngine _plan = new();
    private readonly AttentionEngine _attention = new();
    private readonly AttentionInertiaEngine _attentionInertia = new();
    private readonly SemanticMemory _semantic = new();
    private readonly SelfTalkEngine _selfTalk = new();
    private readonly SelfTalkThrottle _selfTalkThrottle = new();
    private readonly ActionCooldowns _cooldowns = new();
    private readonly PersonalityProfile _personality = PersonalityProfile.LoadLada();
    private readonly VoiceModeResolver _voice = new();
    private readonly HabitSystem _habits = new();
    private readonly MessageDedupeGuard _dedupe = new();
    private readonly CalmBaselineEngine _calmBaseline = new();
    private readonly Dictionary<string, int> _actionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _eventCounts = new(StringComparer.Ordinal);
    private int _anxiousCount;
    private int _calmCount;
    private int _curiousCount;
    private int _statsTicks;
    private double _sumPain;
    private double _sumSafety;
    private double _sumArousal;
    private double _sumThreat;
    private double _sumCalm;
    private double _sumStress;
    private int _painClampedCount;
    private readonly List<double> _painSamples = new(256);
    private bool _sleepConsolidated;
    private string? _lastSelfTalk;

    private HomeostasisDto _homeo = new(0.7, 0.2, 0.3, 0.1, 0.7);
    private AffectDto _affect = new("calm", 0.2, 0.3);
    private long _tick;
    private bool _running = true;
    private bool _stepRequested;
    private double _agencyOffset;
    private string? _outputSinceDiag;

    private readonly List<EpisodeDto> _episodes = new(512);
    private const int EpisodeCapacity = 512;
    private const int DiagnosticTicks = 200;

    public LifeLoop(
        WorldSim world,
        HomeostasisEngine homeostasis,
        InstinctEngine instincts,
        EmotionEngine emotion,
        ActionSelector selector,
        Actuator actuator,
        RewardEngine reward,
        LearningEngine learning,
        Action<LifeStateDto, CancellationToken> broadcast,
        Action<TraceDto, CancellationToken> trace,
        Action<LifeOutputDto, CancellationToken> output,
        Action<string> log)
    {
        _world = world;
        _homeostasis = homeostasis;
        _instincts = instincts;
        _emotion = emotion;
        _selector = selector;
        _actuator = actuator;
        _reward = reward;
        _learning = learning;
        _broadcast = broadcast;
        _trace = trace;
        _output = output;
        _log = log;
    }

    public void Start() => _running = true;
    public void Stop() => _running = false;
    public void Step() => _stepRequested = true;

    public async Task RunAsync(CancellationToken ct)
    {
        const double dtSeconds = 0.2;
        var delayMs = 200;

        while (!ct.IsCancellationRequested)
        {
            if (_running || _stepRequested)
            {
                TickOnce(dtSeconds, ct);
                _stepRequested = false;
                if (_running)
                    await Task.Delay(delayMs, ct);
            }
            else
            {
                await Task.Delay(50, ct);
            }
        }
    }

    private void TickOnce(double dtSeconds, CancellationToken ct)
    {
        var beforeHomeo = _homeo;
        var beforeAffect = _affect;

        _clock.Tick(dtSeconds, _sleep.IsSleeping);
        _sleep.Update(_clock, ref _homeo, ref _affect);
        if (_sleep.EnteredSleep)
        {
            _sleepConsolidated = false;
            _log("ENTER SLEEP");
        }
        if (_sleep.WokeUp)
            _log("WAKE UP");

        if (_sleep.IsSleeping)
        {
            if (!_sleepConsolidated)
            {
                var sums = _memory.Consolidate();
                foreach (var kv in sums)
                {
                    if (kv.Value > 0)
                        _learning.AddBias(kv.Key, 0.02);
                    else if (kv.Value < 0)
                        _learning.AddBias(kv.Key, -0.01);
                }
                _semantic.Consolidate();
                _loop.ResetShortTerm();
                _sleepConsolidated = true;
            }

            var circadian = _clock.Snapshot(true);
            var stateSleep = new LifeStateDto(
                _tick,
                DateTimeOffset.Now,
                _homeo,
                _instincts.Compute(_homeo, _world),
                _affect,
                "sleep",
                0,
                null,
                "",
                1.0 - _affect.Arousal,
                circadian,
                Array.Empty<DeepBrain.Shared.BrainDtos.V3.GoalDto>(),
                null,
                null,
                _world.Events.GetRecent(5),
                _semantic.GetTopNotes(3),
                new DeepBrain.Shared.BrainDtos.V5.CharacterStateDto(
                    _personality.Persona,
                    _habits.GetTopHabits(5),
                    null,
                    0.0,
                    "calm",
                    0,
                    _cooldowns.GetLastTick("emit_message"),
                    _world.Events.ConsumedCount
                ),
                new DeepBrain.Shared.BrainDtos.V5.ClimateDto(_world.CalmLevel, _world.StressLevel),
                new DeepBrain.Shared.BrainDtos.V5.PainSourceDto(0, 0, 0)
            );
            _broadcast(stateSleep, ct);
            _tick++;
            return;
        }

        var circ = _clock.Snapshot(false);
        var preInstincts = _instincts.Compute(_homeo, _world);
        var newEvents = _world.Tick(_tick, circ.Phase, circ.SleepPressure, _sleep.IsSleeping, dtSeconds, preInstincts.Attachment);
        foreach (var ev in newEvents)
            _log($"EVENT: {ev.Type} {ev.Severity:0.00} {ev.Payload}");
        foreach (var ev in newEvents)
            _eventCounts[ev.Type] = _eventCounts.TryGetValue(ev.Type, out var count) ? count + 1 : 1;
        var recentEvents = _world.Events.GetRecent(5);
        if (_tick % 50 == 0)
            EmitTrace("world.climate", new { calm = _world.CalmLevel, stress = _world.StressLevel, lastMajor = _world.LastMajorEvent }, ct);

        _homeo = _homeostasis.Update(_homeo, _world, dtSeconds);
        var painSource = ApplyPainSafetyAdjustments(ref _homeo, circ, recentEvents, dtSeconds);
        var instincts = _instincts.Compute(_homeo, _world);
        instincts = instincts with
        {
            Agency = LifeMath.Clamp01(instincts.Agency - _agencyOffset)
        };

        var computedAffect = _emotion.Compute(instincts, _homeo);
        var (inertAffect, moodInertia) = _inertia.Apply(_affect, computedAffect, instincts.SelfPreservation);
        _affect = inertAffect;
        _personality.ApplyBaselines(ref _affect, ref instincts);
        var topEventSalience = recentEvents.FirstOrDefault()?.Salience ?? 0;
        _calmBaseline.Apply(ref _affect, ref instincts, dtSeconds, _world.StressLevel, topEventSalience);

        var dominantDrive = _driveResolver.Resolve(instincts);
        var goals = _goals.Resolve(_homeo, instincts, dominantDrive, circ.Phase);
        if (_world.CalmLevel > 0.6 && _homeo.Energy > 0.5 && _homeo.Pain < 0.4)
            goals = ApplyExploreRebound(goals);
        var activePlan = _plan.Update(goals, dominantDrive, _loop.LoopPenalty, _sleep.IsSleeping, instincts.SelfPreservation);
        if (_plan.Created)
            _log($"PLAN CREATED: {activePlan?.Strategy} goal={activePlan?.GoalId} ttl={activePlan?.RemainingTicks}");
        if (_plan.Interrupted)
            _log("PLAN INTERRUPTED");

        goals = _personality.BiasGoals(goals, circ.Phase, recentEvents);
        var computedAttention = _attention.Compute(_homeo, instincts, _affect, circ, recentEvents);
        var attention = _attentionInertia.Apply(computedAttention);
        var semanticKey = $"{_affect.Mood}+drive={dominantDrive}+phase={circ.Phase}+focus={attention.Focus1}";
        var cueKey = _habits.ComputeCue(attention, circ, _loop, recentEvents, _homeo);
        var (habitAction, habitStrength, habitId) = _habits.Suggest(cueKey);
        if (habitId is not null)
            habitStrength *= _habits.GetSatiationFactor(habitId, _tick);
        var habitInfluence = _habits.ComputeInfluence(_personality.Persona, habitStrength);
        if (habitAction is null)
            habitInfluence = 0.0;
        habitInfluence = Math.Min(0.4, habitInfluence);
        if (_loop.LoopPenalty > 0.4)
            habitInfluence *= 0.7;
        var voiceMode = _voice.Resolve(_personality.Persona, _affect, instincts, circ);
        var isAnxious = _affect.Mood == "anxious" || instincts.SelfPreservation > 0.8;
        var emitCooldown = ComputeEmitCooldownTicks(voiceMode, _personality.Persona.AttachmentBaseline, isAnxious);
        var allowVariety = _world.Threat < 0.35 && attention.Focus1 != "threat";
        var calmExploreBoost = _world.CalmLevel > 0.6 && _homeo.Energy > 0.5 && _homeo.Pain < 0.4;

        var (action, reason, strategy) = _selector.Choose(
            _homeo,
            instincts,
            _affect,
            _learning,
            _loop,
            _memory,
            _cooldowns,
            _tick,
            habitAction,
            habitStrength,
            habitInfluence,
            emitCooldown,
            allowVariety,
            calmExploreBoost,
            dominantDrive,
            activePlan?.Strategy,
            attention.Focus1,
            _semantic,
            semanticKey
        );
        EmitTrace("decision", new { actionName = action.Name, kind = action.Kind, strength = action.Strength, reason }, ct);

        var outcome = _actuator.Apply(action, ref _homeo, ref _affect);

        if (action.Name == "focus_narrow")
            _agencyOffset = LifeMath.Clamp01(_agencyOffset + 0.1 * action.Strength);
        else
            _agencyOffset = LifeMath.Clamp01(_agencyOffset - 0.02);

        if (action.Name == "explore_signal")
            _world.DampenNovelty(action.Strength);

        var reward = _reward.Compute(beforeHomeo, _homeo);
        reward += ComputeRegulationBonus(action, beforeHomeo, _homeo, beforeAffect, _affect);
        _learning.Update(action.Name, reward);
        _loop.Update(action.Name, reward);
        _cooldowns.Mark(action.Name, _tick);

        outcome = new OutcomeDto(outcome.Action, reward, outcome.Message);

        EmitTrace("homeostasis", _homeo, ct);
        EmitTrace("instincts", instincts, ct);
        EmitTrace("affect", _affect, ct);
        EmitTrace("action", new
        {
            actionName = action.Name,
            kind = action.Kind,
            deltaSummary = new
            {
                Energy = _homeo.Energy - beforeHomeo.Energy,
                Fatigue = _homeo.Fatigue - beforeHomeo.Fatigue,
                Safety = _homeo.Safety - beforeHomeo.Safety,
                Arousal = _homeo.Arousal - beforeHomeo.Arousal,
                Valence = _affect.Valence - beforeAffect.Valence
            }
        }, ct);
        EmitTrace("reward", new { reward }, ct);

        if (action.Kind == "external" && string.IsNullOrWhiteSpace(outcome.Message))
            outcome = new OutcomeDto(outcome.Action, outcome.Reward, $"action={action.Name}");

        if (!string.IsNullOrWhiteSpace(outcome.Message))
        {
            var msg = FormatOutputMessage(outcome.Message!.Trim(), action.Name, voiceMode);
            if (_dedupe.ShouldPublish(_tick, msg))
            {
                _log($"[life] OUTPUT: {msg}");
                EmitTrace("output", new { message = msg, actionName = action.Name }, ct);
                _outputSinceDiag = msg;
                _output(new LifeOutputDto(_tick, DateTimeOffset.Now, msg, action.Name), ct);
            }
        }

        if (action.Name == "emit_message")
        {
            var count = _world.Events.Consume(e => e.Type == "social_ping" && e.Salience > 0.3);
            if (count > 0)
                _log($"EVENT_CONSUMED social_ping tick={_tick}");
        }
        else if (action.Name == "explore_signal")
        {
            var count = _world.Events.Consume(e => (e.Type == "calm_window" || e.Type == "novelty_opportunity") && e.Salience > 0.3);
            if (count > 0)
                _log($"EVENT_CONSUMED explore_window tick={_tick}");
        }
        else if (action.Name == "breathe_slow" && attention.Focus1 == "threat")
        {
            var count = _world.Events.Consume(e => e.Type == "threat_spike" && e.Salience > 0.3);
            if (count > 0)
                _log($"EVENT_CONSUMED threat_spike tick={_tick}");
        }

        var policy = new DeepBrain.Shared.BrainDtos.V2.PolicyContextDto(
            strategy,
            $"drive={dominantDrive} loopPenalty={_loop.LoopPenalty:0.00} -> {strategy}:{action.Name}",
            _loop.LoopCount,
            _loop.LoopPenalty,
            _loop.LastAction,
            _loop.SameActionStreak,
            _loop.AvgRewardShort
        );

        var emitRemaining = Math.Max(0, emitCooldown - (int)(_tick - _cooldowns.GetLastTick("emit_message")));
        var state = new LifeStateDto(
            _tick,
            DateTimeOffset.Now,
            _homeo,
            instincts,
            _affect,
            action.Name,
            reward,
            policy,
            dominantDrive,
            moodInertia,
            circ,
            goals,
            activePlan,
            attention,
            recentEvents,
            _semantic.GetTopNotes(3),
            new DeepBrain.Shared.BrainDtos.V5.CharacterStateDto(
                _personality.Persona,
                _habits.GetTopHabits(5),
                habitId,
                habitInfluence,
                voiceMode,
                emitRemaining,
                _cooldowns.GetLastTick("emit_message"),
                _world.Events.ConsumedCount
            ),
            new DeepBrain.Shared.BrainDtos.V5.ClimateDto(_world.CalmLevel, _world.StressLevel),
            painSource
        );

        var episode = new EpisodeDto(
            _tick,
            beforeHomeo,
            _homeo,
            action,
            reward,
            state.Ts
        );

        AppendEpisode(episode);
        _memory.Add(episode, activePlan?.GoalId ?? "none", strategy);
        _semantic.Update(semanticKey, action.Name, reward);

        if (activePlan is not null)
            _goals.ApplyGoalSatisfaction(activePlan.GoalId, reward > 0 ? 0.05 : -0.02);

        _broadcast(state, ct);

        foreach (var change in _habits.ApplyDecay(_tick))
            _log($"HABIT_DECAY: {change.before.Id} {change.before.Strength:0.00}->{change.after.Strength:0.00}");

        if (_tick < DiagnosticTicks && _tick % 50 == 0)
        {
            var goalsLine = string.Join(",", goals.Select(g => $"{g.Id}:{g.Urgency:0.00}"));
            var planLine = activePlan is null ? "none" : $"{activePlan.Strategy}/{activePlan.GoalId}/{activePlan.RemainingTicks}";
            var topEvent = recentEvents.FirstOrDefault();
            var topEventText = topEvent is null ? "none" : $"{topEvent.Type}/{topEvent.Salience:0.00}";
            var habitLine = habitAction is null ? "none" : $"{habitAction}/{habitStrength:0.00}";
            var line = $"tick={_tick} | phase={circ.Phase} | focus={attention.Focus1}({attention.Intensity:0.00}) | voice={voiceMode} | cue={cueKey} | habit={habitLine} | plan={planLine} | action={action.Name}({action.Kind}) | reward={reward:0.000} | loopPenalty={_loop.LoopPenalty:0.00} | selftalk={(string.IsNullOrWhiteSpace(_lastSelfTalk) ? "no" : "yes")}";
            _log(line);
            if (!string.IsNullOrWhiteSpace(_outputSinceDiag))
            {
                _log($"OUTPUT: {_outputSinceDiag}");
                _outputSinceDiag = null;
            }
            if (_loop.SameActionStreak > 10)
                _log($"LOOP WARNING: strategy={strategy} action={action.Name}");
        }

        var selfTalk = _selfTalk.MaybeSpeak(new SelfTalkContext(
            _sleep.EnteredSleep,
            _sleep.WokeUp,
            _loop.LoopPenalty,
            _affect.Mood,
            instincts.SelfPreservation,
            attention.Focus1,
            recentEvents.Any(e => e.Type == "calm_window"),
            reward
        ), voiceMode, _tick);
        _lastSelfTalk = selfTalk;
        if (!string.IsNullOrWhiteSpace(selfTalk) && _selfTalkThrottle.ShouldSpeak(_tick, selfTalk))
        {
            _log($"SELF TALK: {selfTalk}");
            EmitTrace("selftalk", new { text = selfTalk }, ct);
            _output(new LifeOutputDto(_tick, DateTimeOffset.Now, selfTalk, "selftalk"), ct);
        }

        var habitUpdated = _habits.UpdateAfter(action.Name, reward, cueKey, _tick);
        if (habitUpdated is not null)
            _log($"HABIT_LEARN: {cueKey} -> {habitUpdated.Id} strength={habitUpdated.Strength:0.00} avgReward={habitUpdated.AvgReward:0.000}");

        UpdateStats(action.Name, _affect.Mood, _homeo);
        if (_tick > 0 && _tick % 200 == 0)
            EmitStats();

        _tick++;
    }

    private void EmitTrace(string stage, object data, CancellationToken ct)
    {
        _trace(new TraceDto(_tick, stage, data), ct);
    }

    private static double ComputeRegulationBonus(ActionDto action, HomeostasisDto before, HomeostasisDto after, AffectDto beforeAffect, AffectDto afterAffect)
    {
        var bonus = 0.0;
        if (action.Name == "breathe_slow" && after.Arousal < before.Arousal)
            bonus += 0.03;
        if (action.Name == "rest_short" && after.Energy > before.Energy)
            bonus += 0.03;
        if (action.Name == "reframe_negative" && afterAffect.Valence > beforeAffect.Valence)
            bonus += 0.02;
        return bonus;
    }

    private static IReadOnlyList<DeepBrain.Shared.BrainDtos.V3.GoalDto> ApplyExploreRebound(IReadOnlyList<DeepBrain.Shared.BrainDtos.V3.GoalDto> goals)
    {
        if (goals.Count == 0) return goals;
        var list = goals.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var g = list[i];
            if (g.Id != "explore") continue;
            list[i] = g with { Urgency = LifeMath.Clamp01(g.Urgency + 0.08) };
        }
        return list;
    }

    private DeepBrain.Shared.BrainDtos.V5.PainSourceDto ApplyPainSafetyAdjustments(
        ref HomeostasisDto homeo,
        DeepBrain.Shared.BrainDtos.V3.CircadianDto circ,
        IReadOnlyList<DeepBrain.Shared.BrainDtos.V4.WorldEventDto> eventsList,
        double dtSeconds)
    {
        var threatEvents = eventsList.Where(e => (e.Type == "threat_spike" || e.Type == "micro_threat") && e.Salience > 0.3).ToList();
        var calmEvent = eventsList.Any(e => e.Type == "calm_window" && e.Salience > 0.3);

        var threatComponent = _world.Threat * 0.020 + threatEvents.Sum(e => e.Severity) * 0.015;
        var fatigueComponent = Math.Max(0, homeo.Fatigue - 0.6) * 0.030;
        var sleepComponent = Math.Max(0, circ.SleepPressure - 0.7) * 0.030;

        var baselinePain = 0.12;
        var painReturnRatePerSec = 0.08;
        var decay = 0.04 * dtSeconds;
        var pain = homeo.Pain + dtSeconds * (threatComponent + fatigueComponent + sleepComponent) - decay;
        pain -= (homeo.Pain - baselinePain) * painReturnRatePerSec * dtSeconds;
        if (calmEvent)
            pain -= 0.03 * dtSeconds;
        if (homeo.Safety > 0.8)
            pain -= 0.02 * dtSeconds;

        var safety = homeo.Safety;
        if (calmEvent)
            safety += 0.03 * dtSeconds;
        if (threatEvents.Count > 0)
            safety -= 0.05 * dtSeconds;

        homeo = homeo with
        {
            Pain = LifeMath.Clamp01(pain),
            Safety = LifeMath.Clamp01(safety)
        };

        var total = threatComponent + fatigueComponent + sleepComponent;
        if (total <= 0)
            return new DeepBrain.Shared.BrainDtos.V5.PainSourceDto(0, 0, 0);

        return new DeepBrain.Shared.BrainDtos.V5.PainSourceDto(
            threatComponent / total,
            fatigueComponent / total,
            sleepComponent / total
        );
    }

    private static string FormatOutputMessage(string message, string actionName, string voiceMode)
    {
        if (actionName == "emit_message")
        {
            if (voiceMode == "tender")
                return "мягкий сигнал связи";
            return "подаю сигнал связи";
        }

        return message;
    }

    private static int ComputeEmitCooldownTicks(string voiceMode, double attachmentBaseline, bool isAnxious)
    {
        var baseCooldown = voiceMode switch
        {
            "tender" => 60,
            "fiery" => 75,
            "witty" => 65,
            _ => 60
        };

        var adjust = (0.5 - attachmentBaseline) * 40.0;
        var anxiousBoost = isAnxious ? 20 : 0;
        var value = (int)Math.Round(baseCooldown + adjust + anxiousBoost);
        return Math.Clamp(value, 60, 120);
    }

    private void AppendEpisode(EpisodeDto episode)
    {
        if (_episodes.Count >= EpisodeCapacity)
            _episodes.RemoveAt(0);
        _episodes.Add(episode);
    }

    private void UpdateStats(string actionName, string mood, HomeostasisDto homeo)
    {
        _statsTicks++;
        if (mood == "anxious") _anxiousCount++;
        if (mood == "calm") _calmCount++;
        if (mood == "curious") _curiousCount++;

        _sumPain += homeo.Pain;
        _sumSafety += homeo.Safety;
        _sumArousal += homeo.Arousal;
        _sumThreat += _world.Threat;
        _sumCalm += _world.CalmLevel;
        _sumStress += _world.StressLevel;
        if (homeo.Pain >= 0.999) _painClampedCount++;
        _painSamples.Add(homeo.Pain);

        _actionCounts[actionName] = _actionCounts.TryGetValue(actionName, out var count) ? count + 1 : 1;
    }

    private void EmitStats()
    {
        if (_statsTicks == 0) return;

        var anxiousPct = _anxiousCount / (double)_statsTicks;
        var calmPct = _calmCount / (double)_statsTicks;
        var curiousPct = _curiousCount / (double)_statsTicks;
        var avgPain = _sumPain / _statsTicks;
        var avgSafety = _sumSafety / _statsTicks;
        var avgArousal = _sumArousal / _statsTicks;
        var avgThreat = _sumThreat / _statsTicks;
        var avgCalm = _sumCalm / _statsTicks;
        var avgStress = _sumStress / _statsTicks;
        var p95Pain = ComputeP95(_painSamples);

        var topActions = _actionCounts
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => $"{kv.Key}:{kv.Value}")
            .ToArray();

        var threatCount = _eventCounts.TryGetValue("threat_spike", out var t) ? t : 0;
        var microCount = _eventCounts.TryGetValue("micro_threat", out var m) ? m : 0;
        var calmCount = _eventCounts.TryGetValue("calm_window", out var c) ? c : 0;
        var novCount = _eventCounts.TryGetValue("novelty_opportunity", out var n) ? n : 0;
        var socialCount = _eventCounts.TryGetValue("social_ping", out var s) ? s : 0;

        _log($"STATS(200): anxious={anxiousPct:0.00} calm={calmPct:0.00} curious={curiousPct:0.00} avgPain={avgPain:0.00} p95Pain={p95Pain:0.00} avgSafety={avgSafety:0.00} avgArousal={avgArousal:0.00}");
        _log($"STATS climate: avgThreat={avgThreat:0.00} avgCalm={avgCalm:0.00} avgStress={avgStress:0.00} painClamped={_painClampedCount}");
        _log($"STATS actions: {string.Join(", ", topActions)}");
        _log($"STATS events: threat_spike={threatCount} micro_threat={microCount} calm_window={calmCount} novelty={novCount} social_ping={socialCount}");

        _anxiousCount = 0;
        _calmCount = 0;
        _curiousCount = 0;
        _statsTicks = 0;
        _sumPain = 0;
        _sumSafety = 0;
        _sumArousal = 0;
        _sumThreat = 0;
        _sumCalm = 0;
        _sumStress = 0;
        _painClampedCount = 0;
        _painSamples.Clear();
        _actionCounts.Clear();
        _eventCounts.Clear();
    }

    private static double ComputeP95(List<double> samples)
    {
        if (samples.Count == 0) return 0;
        var ordered = samples.OrderBy(v => v).ToList();
        var index = (int)Math.Floor(0.95 * (ordered.Count - 1));
        return ordered[index];
    }
}

