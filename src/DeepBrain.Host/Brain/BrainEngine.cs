using System.Collections.Concurrent;
using DeepBrain.Host.Brain.Act;
using DeepBrain.Host.Brain.Input;
using DeepBrain.Host.Brain.Perception;
using DeepBrain.Host.Brain.Policy;
using DeepBrain.Host.Brain.State;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Trace;

namespace DeepBrain.Host.Brain;

public sealed class BrainEngine
{
    private readonly InputStore _input;
    private readonly PerceptionEngine _perception = new();
    private readonly StateEstimator _stateEstimator = new();
    private readonly PolicyEngine _policy = new();
    private readonly Actuator _actuator = new();

    private readonly ConcurrentQueue<string> _events = new();
    private readonly BrainStateInternal _state = new();

    private long _tick = 0;
    private readonly long _startedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private string _mode = "stopped";
    private string _lastDecision = "none";
    private ActResult? _lastAct;

    public BrainEngine(InputStore input) => _input = input;

    public void Start() => _mode = "running";
    public void Stop()  => _mode = "stopped";

    public BrainStateDto GetState() => new(
        Tick: _tick,
        UptimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startedMs,
        Mode: _mode,
        LastDecision: _lastDecision
    );

    public List<TraceDto> TickWithTrace(bool isForced)
    {
        var traces = new List<TraceDto>(8);

        if (_mode != "running" && !isForced)
            return traces;

        _tick++;

        var evs = DrainEvents(max: 32);
        var input = _input.GetSnapshot();

        var (percept, drives) = _perception.Sense(_tick, input, evs);
        traces.Add(new TraceDto(_tick, "perception", new { timeUtc = percept.TimeUtc, input, events = evs }));

        _stateEstimator.UpdateHomeostasis(_state, drives, _lastAct);
        traces.Add(new TraceDto(_tick, "state", new { mode = _mode, stress = _state.Stress, energy = _state.Energy, focus = _state.Focus, mood = _state.Mood }));

        var decision = _policy.Decide(percept, _state);
        _lastDecision = decision.Name;
        traces.Add(new TraceDto(_tick, "policy", new { decision = decision.Name, confidence = decision.Confidence, reason = decision.Reason }));

        _lastAct = _actuator.Act(decision.Name);
        traces.Add(new TraceDto(_tick, "act", new { done = _lastAct.Done }));

        return traces;
    }

    public void EnqueueEvent(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _events.Enqueue(name.Trim().ToLowerInvariant());
    }

    private List<string> DrainEvents(int max)
    {
        var list = new List<string>(Math.Min(max, 16));
        while (list.Count < max && _events.TryDequeue(out var ev))
            list.Add(ev);
        return list;
    }
}
