using DeepBrain.Shared.Input;

namespace DeepBrain.Host.Brain.Perception;

public sealed class PerceptionEngine
{
    public Percept Sense(long tick, BrainInputDto input)
        => new(DateTimeOffset.UtcNow, tick, input);
}
