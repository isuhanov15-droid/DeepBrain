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
        safety = LifeMath.Clamp01(safety - dtSeconds * (0.04 * world.Threat) + dtSeconds * 0.005);
        pain = LifeMath.Clamp01(pain + dtSeconds * (0.02 * fatigue + 0.05 * world.Threat) - dtSeconds * 0.01);
        arousal = LifeMath.Clamp01(arousal + dtSeconds * (0.02 * world.Noise - 0.01 * safety));

        return new HomeostasisDto(energy, fatigue, arousal, pain, safety);
    }
}
