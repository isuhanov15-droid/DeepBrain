using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V5;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed class StateVectorizer
{
    public const int InputDim = 38;
    public const string Version = "v1";

    public float[] Encode(StateVectorInput input)
    {
        var v = new float[InputDim];
        var i = 0;

        v[i++] = Clamp01(input.Homeostasis.Energy);
        v[i++] = Clamp01(input.Homeostasis.Fatigue);
        v[i++] = Clamp01(input.Homeostasis.Safety);
        v[i++] = Clamp01(input.Homeostasis.Pain);
        v[i++] = Clamp01(input.Homeostasis.Arousal);
        v[i++] = ClampSigned(input.Affect.Valence);
        v[i++] = Clamp01(input.MoodInertia);
        v[i++] = Clamp01(input.Circadian.SleepPressure);

        v[i++] = Clamp01(input.Climate.Calm);
        v[i++] = Clamp01(input.Climate.Stress);
        v[i++] = Clamp01(input.Climate.Tension);
        v[i++] = Clamp01(input.Climate.BaselineTension);

        v[i++] = Clamp01(input.Attention.Intensity);
        v[i++] = Clamp01(input.Attention.ThreatIntensity);

        var focus = input.Attention.Focus1 ?? "neutral";
        v[i++] = focus == "threat" ? 1f : 0f;
        v[i++] = focus == "novelty" ? 1f : 0f;
        v[i++] = focus == "social" ? 1f : 0f;
        v[i++] = focus == "body" ? 1f : 0f;
        v[i++] = focus == "agency" ? 1f : 0f;
        v[i++] = focus == "neutral" ? 1f : 0f;

        v[i++] = Clamp01(input.LoopPenalty);
        v[i++] = ClampSigned(input.AvgRewardShort);

        v[i++] = Clamp01(input.Instincts.SelfPreservation);
        v[i++] = Clamp01(input.Instincts.EnergyConservation);
        v[i++] = Clamp01(input.Instincts.Exploration);
        v[i++] = Clamp01(input.Instincts.Attachment);
        v[i++] = Clamp01(input.Instincts.Agency);

        v[i++] = Clamp01(input.Personality.Warmth);
        v[i++] = Clamp01(input.Personality.Fire);
        v[i++] = Clamp01(input.Personality.Humor);

        v[i++] = Clamp01(input.HabitInfluence);
        v[i++] = Clamp01(input.TopEventSalience);

        var tod = Clamp01(input.Circadian.TimeOfDay);
        var angle = tod * MathF.Tau;
        v[i++] = MathF.Sin(angle);
        v[i++] = MathF.Cos(angle);

        v[i++] = Clamp01(input.Appraisal.Threat);
        v[i++] = Clamp01(input.Appraisal.Novelty);
        v[i++] = Clamp01(input.Appraisal.Social);
        v[i++] = Clamp01(input.Appraisal.Fatigue);

        return v;
    }

    private static float Clamp01(double value) => (float)Math.Clamp(value, 0.0, 1.0);
    private static float ClampSigned(double value) => (float)Math.Clamp(value, -1.0, 1.0);
}

public sealed record StateVectorInput(
    HomeostasisDto Homeostasis,
    InstinctsDto Instincts,
    AffectDto Affect,
    double MoodInertia,
    CircadianDto Circadian,
    ClimateDto Climate,
    AttentionDto Attention,
    double LoopPenalty,
    double AvgRewardShort,
    PersonalityDto Personality,
    double HabitInfluence,
    double TopEventSalience,
    AppraisalDto Appraisal
);
