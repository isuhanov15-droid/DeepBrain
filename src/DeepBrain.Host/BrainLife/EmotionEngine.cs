using System.Linq;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed class EmotionEngine
{
    public AffectDto Compute(
        InstinctsDto instincts,
        HomeostasisDto homeo,
        double climateCalm,
        double climateStress,
        AttentionDto attention,
        IReadOnlyList<WorldEventDto> recentEvents,
        MoodConfig mood,
        AppraisalDto appraisal)
    {
        var valenceRaw = 0.5
                         + 0.35 * (homeo.Energy - homeo.Pain)
                         - 0.2 * instincts.SelfPreservation
                         - 0.35 * appraisal.Threat
                         - 0.15 * appraisal.Fatigue
                         + 0.15 * appraisal.Novelty
                         + 0.15 * appraisal.Social;
        var valence = LifeMath.Clamp01(valenceRaw) * 2 - 1;

        var arousalRaw = homeo.Arousal
                         + 0.25 * appraisal.Threat
                         + 0.2 * appraisal.Novelty
                         - 0.15 * appraisal.Fatigue;
        var arousal = LifeMath.Clamp01(arousalRaw);

        var threatIntensity = attention.ThreatIntensity;
        var noveltySalience = recentEvents.FirstOrDefault(e => e.Type == "novelty_opportunity")?.Salience ?? 0;
        var socialSalience = recentEvents.FirstOrDefault(e => e.Type == "social_ping")?.Salience ?? 0;

        var moodName = "neutral";
        var calmGate = homeo.Pain < mood.CalmPainMax && climateCalm > 0.6 && threatIntensity < 0.35;
        if (!calmGate && (homeo.Pain > mood.AnxiousPainMin || (threatIntensity > mood.AnxiousThreatIntensityMin && climateStress > mood.AnxiousStressMin)))
            moodName = "anxious";
        else if (valence >= mood.CalmValenceMin && arousal <= mood.CalmArousalMax && homeo.Pain < mood.CalmPainMax)
            moodName = "calm";
        else if (noveltySalience > mood.CuriousNoveltyMin && arousal >= mood.CuriousArousalMin && arousal <= mood.CuriousArousalMax && homeo.Pain < 0.25)
            moodName = "curious";
        else if (socialSalience > mood.TenderSocialMin && instincts.Attachment > 0.2 && arousal <= mood.TenderArousalMax)
            moodName = "tender";
        else if (instincts.EnergyConservation > 0.7)
            moodName = "low";
        else if (instincts.Agency > 0.7)
            moodName = "frustrated";

        if (calmGate && moodName == "anxious")
            moodName = "calm";

        return new AffectDto(moodName, valence, arousal);
    }
}
