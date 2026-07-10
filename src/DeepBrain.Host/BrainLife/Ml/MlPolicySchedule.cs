namespace DeepBrain.Host.BrainLife.Ml;

/// <summary>
/// Keeps policy influence and exploration aligned with the amount of useful
/// training evidence, including evidence restored from a checkpoint.
/// </summary>
public static class MlPolicySchedule
{
    public static double ComputeNetWeight(MlConfig config, int bufferSize, long trainSteps)
    {
        var warmup = Math.Max(1.0, config.NetWeightWarmup);
        var effectiveExperience = Math.Max((long)Math.Max(0, bufferSize), Math.Max(0L, trainSteps));
        var progress = Math.Clamp(effectiveExperience / warmup, 0.0, 1.0);
        return Math.Clamp(config.NetWeightMax, 0.0, 1.0) * progress;
    }

    public static bool CanDecayExploration(MlConfig config, int bufferSize, long trainSteps)
    {
        if (trainSteps > 0)
            return true;

        return bufferSize >= Math.Max(1, config.BatchSize);
    }

    public static double NextEpsilon(MlConfig config, double current, int bufferSize, long trainSteps)
    {
        var explorationDisabled = config.EpsilonStart <= 0
            && config.EpsilonEnd <= 0
            && config.EpsilonMin <= 0;
        if (explorationDisabled)
            return 0;

        var epsilon = current < 0 ? Math.Max(0, config.EpsilonStart) : current;
        var minimum = config.EpsilonMin > 0 ? config.EpsilonMin : Math.Max(0, config.EpsilonEnd);
        if (CanDecayExploration(config, bufferSize, trainSteps))
            epsilon = Math.Max(minimum, epsilon - Math.Max(0, config.EpsilonDecay));

        return epsilon;
    }
}
