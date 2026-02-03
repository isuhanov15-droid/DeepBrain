using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class HomeostasisEngine
{
    public HomeostasisDto Update(HomeostasisDto prev, WorldSim world, double dtSeconds)
    {
        var energy = prev.Energy;
        var fatigue = prev.Fatigue;
        var arousal = prev.Arousal;
        var pain = prev.Pain;
        var safety = prev.Safety;

        fatigue = LifeMath.Clamp01(fatigue + dtSeconds * (0.01 + 0.05 * arousal));
        energy = LifeMath.Clamp01(energy - dtSeconds * (0.01 + 0.05 * fatigue + 0.03 * arousal));
        arousal = LifeMath.Clamp01(arousal + dtSeconds * (0.02 * world.Noise - 0.01 * safety));

        return new HomeostasisDto(energy, fatigue, arousal, pain, safety);
    }
}
