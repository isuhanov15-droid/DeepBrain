using DeepBrain.Host.Brain.Perception;

namespace DeepBrain.Host.Brain.Perception;

public sealed class PerceptionEngine
{
    public Percept Sense(long tick) => new(DateTimeOffset.UtcNow, tick);
}
