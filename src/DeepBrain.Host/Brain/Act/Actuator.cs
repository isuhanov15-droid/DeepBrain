using DeepBrain.Host.Brain.Act;

namespace DeepBrain.Host.Brain.Act;

public sealed class Actuator
{
    public ActResult Act(string decisionName) => new(true);
}
