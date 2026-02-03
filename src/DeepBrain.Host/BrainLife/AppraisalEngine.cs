using System.Linq;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed class AppraisalEngine
{
    public AppraisalDto Compute(HomeostasisDto homeo, InstinctsDto instincts, WorldSim world, CircadianDto circ, IReadOnlyList<WorldEventDto> recentEvents)
    {
        var threatEvent = recentEvents.FirstOrDefault(e => e.Type is "threat_spike" or "micro_threat");
        var noveltyEvent = recentEvents.FirstOrDefault(e => e.Type == "novelty_opportunity" || e.Type == "calm_window");
        var socialEvent = recentEvents.FirstOrDefault(e => e.Type == "social_ping");
        var fatigueEvent = recentEvents.FirstOrDefault(e => e.Type == "fatigue_wave");

        var threat = LifeMath.Clamp01(world.Threat * 0.55 + world.StressLevel * 0.25 + (threatEvent?.Salience ?? 0) * 0.45 + instincts.SelfPreservation * 0.25);
        var novelty = LifeMath.Clamp01(world.Novelty * 0.35 + world.CalmLevel * 0.25 + (noveltyEvent?.Salience ?? 0) * 0.6 + instincts.Exploration * 0.25);
        var social = LifeMath.Clamp01(world.SocialPresence * 0.3 + (socialEvent?.Salience ?? 0) * 0.7 + instincts.Attachment * 0.25);
        var fatigue = LifeMath.Clamp01(homeo.Fatigue * 0.7 + circ.SleepPressure * 0.4 + (fatigueEvent?.Salience ?? 0) * 0.4);

        return new AppraisalDto(threat, novelty, social, fatigue);
    }
}
