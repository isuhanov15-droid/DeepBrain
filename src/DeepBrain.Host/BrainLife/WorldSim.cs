namespace DeepBrain.Host.BrainLife;

public sealed class WorldSim
{
    private readonly Random _rng;

    public double Noise { get; private set; } = 0.2;
    public double Novelty { get; private set; } = 0.5;
    public double SocialPresence { get; private set; } = 0.4;
    public double Threat { get; private set; } = 0.1;

    public WorldSim(int seed)
    {
        _rng = new Random(seed);
    }

    public void Tick(long tick)
    {
        Noise = LifeMath.Clamp01(Noise + RandDelta(0.01));
        Novelty = LifeMath.Clamp01(Novelty + RandDelta(0.02));
        SocialPresence = LifeMath.Clamp01(SocialPresence + RandDelta(0.01));
        Threat = LifeMath.Clamp01(Threat + RandDelta(0.005));

        if (_rng.NextDouble() < 0.02)
            Threat = LifeMath.Clamp01(Threat + 0.2 + _rng.NextDouble() * 0.2);

        if (tick % 200 == 0)
            Novelty = LifeMath.Clamp01(Novelty + 0.1);
    }

    public void DampenNovelty(double strength)
    {
        Novelty = LifeMath.Clamp01(Novelty - 0.3 * strength);
    }

    private double RandDelta(double scale)
    {
        return (_rng.NextDouble() * 2.0 - 1.0) * scale;
    }
}
