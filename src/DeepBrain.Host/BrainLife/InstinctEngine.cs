using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class InstinctEngine
{
    public InstinctsDto Compute(HomeostasisDto homeo, WorldSim world)
    {
        var selfPreservation = LifeMath.Clamp01((1 - homeo.Safety) + world.Threat);
        var energyConservation = LifeMath.Clamp01(homeo.Fatigue + (1 - homeo.Energy));
        var exploration = LifeMath.Clamp01(0.6 - world.Novelty + (homeo.Energy - homeo.Fatigue));
        var attachment = LifeMath.Clamp01(1 - world.SocialPresence);
        var agency = LifeMath.Clamp01((world.Threat + (1 - homeo.Safety)) - homeo.Energy);

        return new InstinctsDto(
            selfPreservation,
            energyConservation,
            exploration,
            attachment,
            agency
        );
    }
}
