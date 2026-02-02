using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class EmotionEngine
{
    public AffectDto Compute(InstinctsDto instincts, HomeostasisDto homeo)
    {
        var mood = "calm";
        if (instincts.SelfPreservation > 0.7)
            mood = "anxious";
        else if (instincts.Exploration > 0.6 && homeo.Safety > 0.5)
            mood = "curious";
        else if (instincts.EnergyConservation > 0.7)
            mood = "low";
        else if (instincts.Agency > 0.7)
            mood = "frustrated";

        var valence = LifeMath.Clamp01(0.5 + 0.4 * (homeo.Energy - homeo.Pain) - 0.2 * instincts.SelfPreservation) * 2 - 1;
        var arousal = LifeMath.Clamp01(homeo.Arousal + 0.2 * instincts.SelfPreservation);

        return new AffectDto(mood, valence, arousal);
    }
}
