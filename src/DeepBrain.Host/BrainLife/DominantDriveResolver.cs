using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class DominantDriveResolver
{
    public string Resolve(InstinctsDto instincts)
    {
        var max = instincts.SelfPreservation;
        var name = "self_preservation";

        if (instincts.EnergyConservation > max) { max = instincts.EnergyConservation; name = "energy_conservation"; }
        if (instincts.Exploration > max) { max = instincts.Exploration; name = "exploration"; }
        if (instincts.Attachment > max) { max = instincts.Attachment; name = "attachment"; }
        if (instincts.Agency > max) { max = instincts.Agency; name = "agency"; }

        return name;
    }
}
