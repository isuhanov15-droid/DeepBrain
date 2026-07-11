using System.Linq;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

public sealed class AttentionEngine
{
    public AttentionDto Compute(
        HomeostasisDto homeo,
        InstinctsDto instincts,
        AffectDto affect,
        CircadianDto circadian,
        IReadOnlyList<WorldEventDto> recentEvents,
        double worldTension,
        double dtSeconds)
    {
        var focus1 = "body";
        var focus2 = (string?)null;
        var intensity = 0.35;
        var reason = "baseline";
        var threatIntensity = LifeMath.Clamp01(0.2 + worldTension * 0.4 + instincts.SelfPreservation * 0.2);

        if (instincts.SelfPreservation > 0.7 || HasEvent(recentEvents, "threat_spike") || HasEvent(recentEvents, "micro_threat"))
        {
            focus1 = "threat";
            intensity = LifeMath.Clamp01(0.5 + threatIntensity * 0.5);
            reason = "self_preservation/threat_event";
        }
        else if (HasEvent(recentEvents, "social_ping", 0.3))
        {
            focus1 = "social";
            intensity = 0.75;
            reason = "social_ping";
        }
        else if (instincts.Exploration > 0.6 && (HasEvent(recentEvents, "calm_window") || HasEvent(recentEvents, "novelty_opportunity")))
        {
            focus1 = "novelty";
            intensity = 0.7;
            reason = "exploration/calm_window";
        }
        else if (homeo.Fatigue > 0.6 || circadian.SleepPressure > 0.6)
        {
            focus1 = "body";
            intensity = 0.7;
            reason = "fatigue/sleep_pressure";
        }
        else if (instincts.Agency > 0.6 && affect.Valence < 0)
        {
            focus1 = "agency";
            intensity = 0.6;
            reason = "agency/negative_valence";
        }

        if (focus1 != "threat" && !HasEvent(recentEvents, "threat_spike") && !HasEvent(recentEvents, "micro_threat") && worldTension < 0.35)
        {
            threatIntensity = Math.Max(0.2, threatIntensity - 0.2 * dtSeconds);
        }

        if (focus1 == "threat" && homeo.Fatigue > 0.6)
            focus2 = "body";
        else if (focus1 == "novelty" && instincts.Attachment > 0.5)
            focus2 = "social";

        return new AttentionDto(focus1, focus2, intensity, reason, focus1, threatIntensity);
    }

    private static bool HasEvent(IReadOnlyList<WorldEventDto> eventsList, string type, double minimumSalience = 0.2)
    {
        return eventsList.Any(e => e.Type == type && e.Salience > minimumSalience);
    }
}
