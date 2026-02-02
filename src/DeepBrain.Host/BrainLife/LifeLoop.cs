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
        _world.Tick(_tick);

        _homeo = _homeostasis.Update(_homeo, _world, dtSeconds);
        var instincts = _instincts.Compute(_homeo, _world);
        instincts = instincts with
        {
            Agency = LifeMath.Clamp01(instincts.Agency - _agencyOffset)
        };

        _affect = _emotion.Compute(instincts, _homeo);

        var (action, reason) = _selector.Choose(_homeo, instincts, _affect, _learning);
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

        var state = new LifeStateDto(
            _tick,
            DateTimeOffset.Now,
            _homeo,
            instincts,
            _affect,
            action.Name,
            reward
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

        _broadcast(state, ct);

        if (_tick < DiagnosticTicks && _tick % 20 == 0)
        {
            var (bestAction, bestEma) = _learning.GetBest();
            var reasonShort = reason.Length > 48 ? reason[..48] + "..." : reason;
            var line = $"tick={_tick} | mood={_affect.Mood} | energy={_homeo.Energy:0.00} | fatigue={_homeo.Fatigue:0.00} | safety={_homeo.Safety:0.00} | action={action.Name}({action.Kind}) | reward={reward:0.000} | emaBest={bestAction}:{bestEma:0.000} | reason={reasonShort}";
            _log(line);
            if (!string.IsNullOrWhiteSpace(_outputSinceDiag))
            {
                _log($"OUTPUT: {_outputSinceDiag}");
                _outputSinceDiag = null;
            }
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
