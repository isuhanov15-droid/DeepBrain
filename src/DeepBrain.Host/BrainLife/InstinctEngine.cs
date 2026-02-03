using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class InstinctEngine
{
    public InstinctsDto Compute(HomeostasisDto homeo, WorldSim world, DrivesConfig? drives = null)
    {
        var selfPreservation = LifeMath.Clamp01((1 - homeo.Safety) + world.Threat);
        var energyConservation = LifeMath.Clamp01(homeo.Fatigue + (1 - homeo.Energy));
        var exploration = LifeMath.Clamp01(0.6 - world.Novelty + (homeo.Energy - homeo.Fatigue));
        var attachment = LifeMath.Clamp01(1 - world.SocialPresence);
        var agency = LifeMath.Clamp01((world.Threat + (1 - homeo.Safety)) - homeo.Energy);

        if (drives is not null)
        {
            selfPreservation *= drives.WeightSelfPreservation;
            exploration *= drives.WeightExploration;
            attachment *= drives.WeightAttachment;
            agency *= drives.WeightAgency;

            var temp = Math.Max(0.05, drives.SoftmaxTemperature);
            var sp = Math.Exp(selfPreservation / temp);
            var ex = Math.Exp(exploration / temp);
            var at = Math.Exp(attachment / temp);
            var ag = Math.Exp(agency / temp);
            var sum = sp + ex + at + ag;
            if (sum > 0)
            {
                selfPreservation = LifeMath.Clamp01(sp / sum);
                exploration = LifeMath.Clamp01(ex / sum);
                attachment = LifeMath.Clamp01(at / sum);
                agency = LifeMath.Clamp01(ag / sum);
            }
        }

        return new InstinctsDto(
            selfPreservation,
            energyConservation,
            exploration,
            attachment,
            agency
        );
    }
}
