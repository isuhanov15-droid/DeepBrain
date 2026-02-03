using System.Linq;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

public sealed class EmotionEngine
{
    public AffectDto Compute(InstinctsDto instincts, HomeostasisDto homeo, double climateCalm, double climateStress, AttentionDto attention, IReadOnlyList<WorldEventDto> recentEvents)
    {
        var valence = LifeMath.Clamp01(0.5 + 0.4 * (homeo.Energy - homeo.Pain) - 0.2 * instincts.SelfPreservation) * 2 - 1;
        var arousal = LifeMath.Clamp01(homeo.Arousal + 0.2 * instincts.SelfPreservation);

        var threatIntensity = attention.ThreatIntensity;
        var noveltySalience = recentEvents.FirstOrDefault(e => e.Type == "novelty_opportunity")?.Salience ?? 0;
        var socialSalience = recentEvents.FirstOrDefault(e => e.Type == "social_ping")?.Salience ?? 0;

        var mood = "neutral";
        var calmGate = homeo.Pain < 0.15 && climateCalm > 0.6 && threatIntensity < 0.35;
        if (!calmGate && (homeo.Pain > 0.35 || (threatIntensity > 0.6 && climateStress > 0.5)))
            mood = "anxious";
        else if (valence >= 0.05 && arousal <= 0.30 && homeo.Pain < 0.2)
            mood = "calm";
        else if (noveltySalience > 0.35 && arousal >= 0.25 && arousal <= 0.45 && homeo.Pain < 0.25)
            mood = "curious";
        else if (socialSalience > 0.35 && instincts.Attachment > 0.2 && arousal <= 0.35)
            mood = "tender";
        else if (instincts.EnergyConservation > 0.7)
            mood = "low";
        else if (instincts.Agency > 0.7)
            mood = "frustrated";

        if (calmGate && mood == "anxious")
            mood = "calm";

        return new AffectDto(mood, valence, arousal);
    }
}
