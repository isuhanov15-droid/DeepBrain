using DeepBrain.Host.Brain.Policy;

namespace DeepBrain.Host.Brain.Policy;

public sealed class PolicyEngine
{
    public Decision Decide(long tick)
        => new((tick % 2 == 0) ? "observe" : "wait");
}
