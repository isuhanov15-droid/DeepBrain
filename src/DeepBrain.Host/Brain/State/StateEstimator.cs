using DeepBrain.Host.Brain.State;

namespace DeepBrain.Host.Brain.State;

public sealed class StateEstimator
{
    public BrainStateInternal Estimate(string mode, long tick) => new(mode, tick);
}
