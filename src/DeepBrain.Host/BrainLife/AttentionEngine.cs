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
        IReadOnlyList<WorldEventDto> recentEvents)
    {
        var focus1 = "body";
        var focus2 = (string?)null;
        var intensity = 0.4;
        var reason = "baseline";

        if (instincts.SelfPreservation > 0.7 || HasEvent(recentEvents, "threat_spike"))
        {
            focus1 = "threat";
            intensity = 0.8;
            reason = "self_preservation/threat_spike";
        }
        else if (instincts.Exploration > 0.6 && (HasEvent(recentEvents, "calm_window") || HasEvent(recentEvents, "novelty_opportunity")))
        {
            focus1 = "novelty";
            intensity = 0.7;
            reason = "exploration/calm_window";
        }
        else if (instincts.Attachment > 0.6 && HasEvent(recentEvents, "social_ping"))
        {
            focus1 = "social";
            intensity = 0.7;
            reason = "attachment/social_ping";
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

        if (focus1 == "threat" && homeo.Fatigue > 0.6)
            focus2 = "body";
        else if (focus1 == "novelty" && instincts.Attachment > 0.5)
            focus2 = "social";

        return new AttentionDto(focus1, focus2, intensity, reason);
    }

    private static bool HasEvent(IReadOnlyList<WorldEventDto> eventsList, string type)
    {
        return eventsList.Any(e => e.Type == type && e.Salience > 0.2);
    }
}
