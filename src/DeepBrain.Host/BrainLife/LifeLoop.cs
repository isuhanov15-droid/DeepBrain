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
    private readonly SemanticMemory _semantic = new();
    private readonly SelfTalkEngine _selfTalk = new();
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
                _semantic.GetTopNotes(3)
            );
            _broadcast(stateSleep, ct);
            _tick++;
            return;
        }

        var circ = _clock.Snapshot(false);
        var newEvents = _world.Tick(_tick, circ.Phase, circ.SleepPressure, _sleep.IsSleeping);
        foreach (var ev in newEvents)
            _log($"EVENT: {ev.Type} {ev.Severity:0.00} {ev.Payload}");

        _homeo = _homeostasis.Update(_homeo, _world, dtSeconds);
        var instincts = _instincts.Compute(_homeo, _world);
        instincts = instincts with
        {
            Agency = LifeMath.Clamp01(instincts.Agency - _agencyOffset)
        };

        var computedAffect = _emotion.Compute(instincts, _homeo);
        var (inertAffect, moodInertia) = _inertia.Apply(_affect, computedAffect, instincts.SelfPreservation);
        _affect = inertAffect;

        var dominantDrive = _driveResolver.Resolve(instincts);
        var goals = _goals.Resolve(_homeo, instincts, dominantDrive, circ.Phase);
        var activePlan = _plan.Update(goals, dominantDrive, _loop.LoopPenalty, _sleep.IsSleeping, instincts.SelfPreservation);
        if (_plan.Created)
            _log($"PLAN CREATED: {activePlan?.Strategy} goal={activePlan?.GoalId} ttl={activePlan?.RemainingTicks}");
        if (_plan.Interrupted)
            _log("PLAN INTERRUPTED");

        var recentEvents = _world.Events.GetRecent(5);
        var attention = _attention.Compute(_homeo, instincts, _affect, circ, recentEvents);
        var semanticKey = $"{_affect.Mood}+drive={dominantDrive}+phase={circ.Phase}+focus={attention.Focus1}";

        var (action, reason, strategy) = _selector.Choose(
            _homeo,
            instincts,
            _affect,
            _learning,
            _loop,
            _memory,
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
        _learning.Update(action.Name, reward);
        _loop.Update(action.Name, reward);

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
            var msg = outcome.Message!.Trim();
            _log($"[life] OUTPUT: {msg}");
            EmitTrace("output", new { message = msg, actionName = action.Name }, ct);
            _outputSinceDiag = msg;
            _output(new LifeOutputDto(_tick, DateTimeOffset.Now, msg, action.Name), ct);
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
            _semantic.GetTopNotes(3)
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

        if (_tick < DiagnosticTicks && _tick % 50 == 0)
        {
            var goalsLine = string.Join(",", goals.Select(g => $"{g.Id}:{g.Urgency:0.00}"));
            var planLine = activePlan is null ? "none" : $"{activePlan.Strategy}/{activePlan.GoalId}/{activePlan.RemainingTicks}";
            var line = $"tick={_tick} | phase={circ.Phase} | sleeping={circ.IsSleeping} | focus={attention.Focus1} | eventTop={(recentEvents.LastOrDefault()?.Type ?? "none")} | plan={planLine} | action={action.Name}({action.Kind}) | reward={reward:0.000} | loopPenalty={_loop.LoopPenalty:0.00} | selftalk={(string.IsNullOrWhiteSpace(_lastSelfTalk) ? "no" : "yes")}";
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
        ));
        _lastSelfTalk = selfTalk;
        if (!string.IsNullOrWhiteSpace(selfTalk))
        {
            _log($"selftalk: {selfTalk}");
            EmitTrace("selftalk", new { text = selfTalk }, ct);
            _output(new LifeOutputDto(_tick, DateTimeOffset.Now, $"selftalk: {selfTalk}", "selftalk"), ct);
        }

        _tick++;
    }

    private void EmitTrace(string stage, object data, CancellationToken ct)
    {
        _trace(new TraceDto(_tick, stage, data), ct);
    }

    private void AppendEpisode(EpisodeDto episode)
    {
        if (_episodes.Count >= EpisodeCapacity)
            _episodes.RemoveAt(0);
        _episodes.Add(episode);
    }
}
