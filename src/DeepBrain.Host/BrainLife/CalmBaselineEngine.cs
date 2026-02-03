using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class CalmBaselineEngine
{
    public void Apply(ref AffectDto affect, ref InstinctsDto instincts, double dtSeconds, double stressLevel, double topEventSalience)
    {
        if (topEventSalience >= 0.2 || stressLevel >= 0.4)
            return;

        var rate = Math.Min(0.2, 0.8 * dtSeconds);
        var targetArousal = 0.2;
        var targetValence = 0.1;
        var targetSelf = 0.55;

        affect = affect with
        {
            Arousal = LifeMath.Clamp01(affect.Arousal + (targetArousal - affect.Arousal) * rate),
            Valence = Math.Clamp(affect.Valence + (targetValence - affect.Valence) * rate, -1, 1)
        };

        instincts = instincts with
        {
            SelfPreservation = LifeMath.Clamp01(instincts.SelfPreservation + (targetSelf - instincts.SelfPreservation) * rate)
        };
    }
}
