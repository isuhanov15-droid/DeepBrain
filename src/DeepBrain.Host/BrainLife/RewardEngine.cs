using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class RewardEngine
{
    public double Compute(HomeostasisDto before, HomeostasisDto after)
    {
        var reward =
            (after.Energy - before.Energy) +
            (after.Safety - before.Safety) -
            (after.Fatigue - before.Fatigue) * 0.5 -
            (after.Pain - before.Pain) * 0.5;

        return reward;
    }
}
