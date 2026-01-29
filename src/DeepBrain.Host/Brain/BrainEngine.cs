using DeepBrain.Host.Brain.Act;
using DeepBrain.Host.Brain.Perception;
using DeepBrain.Host.Brain.Policy;
using DeepBrain.Host.Brain.State;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Trace;

namespace DeepBrain.Host.Brain;

public sealed class BrainEngine
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    private long _tick;
    private bool _isRunning;
    private string _mode = "idle";
    private string _lastDecision = "none";

    // Модули
    private readonly PerceptionEngine _perception = new();
    private readonly StateEstimator _stateEstimator = new();
    private readonly PolicyEngine _policy = new();
    private readonly Actuator _actuator = new();

    public bool IsRunning => _isRunning;

    public void Start()
    {
        _isRunning = true;
        _mode = "running";
    }

    public void Stop()
    {
        _isRunning = false;
        _mode = "idle";
    }
    public void Step(bool isForced = false)
    {
        // Совместимость: Step вызывает тик, но trace не наружу
        foreach (var _ in TickWithTrace(isForced))
        {
            // намеренно игнорируем trace
        }
    }

    // Если хочешь удобно собирать trace в Program.cs:
    public List<TraceDto> StepWithTrace(bool isForced = false)
    {
        return TickWithTrace(isForced).ToList();
    }


    public BrainStateDto GetState()
    {
        var uptimeMs = (long)(DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds;
        return new BrainStateDto(_tick, uptimeMs, _mode, _lastDecision);
    }

    public IEnumerable<TraceDto> TickWithTrace(bool isForced = false)
    {
        if (!_isRunning && !isForced)
            yield break;

        _tick++;

        // 1) Perception
        var percept = _perception.Sense(_tick);
        yield return new TraceDto(_tick, "perception", new { timeUtc = percept.TimeUtc });

        // 2) State estimation
        var state = _stateEstimator.Estimate(_mode, _tick);
        yield return new TraceDto(_tick, "state", new { mode = state.Mode });

        // 3) Policy decision
        var decision = _policy.Decide(_tick);
        _lastDecision = decision.Name;
        yield return new TraceDto(_tick, "policy", new { decision = decision.Name });

        // 4) Act
        var act = _actuator.Act(decision.Name);
        yield return new TraceDto(_tick, "act", new { done = act.Done });
    }
}
