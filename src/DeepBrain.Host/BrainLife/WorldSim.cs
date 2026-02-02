namespace DeepBrain.Host.BrainLife;

public sealed class WorldSim
{
    private readonly Random _rng;
    private readonly WorldEventsQueue _events = new();

    public double Noise { get; private set; } = 0.2;
    public double Novelty { get; private set; } = 0.5;
    public double SocialPresence { get; private set; } = 0.4;
    public double Threat { get; private set; } = 0.1;

    public WorldEventsQueue Events => _events;

    public WorldSim(int seed)
    {
        _rng = new Random(seed);
    }

    public IReadOnlyList<DeepBrain.Shared.BrainDtos.V4.WorldEventDto> Tick(long tick, string phase, double sleepPressure, bool isSleeping)
    {
        Noise = LifeMath.Clamp01(Noise + RandDelta(0.01));
        Novelty = LifeMath.Clamp01(Novelty + RandDelta(0.02));
        SocialPresence = LifeMath.Clamp01(SocialPresence + RandDelta(0.01));
        Threat = LifeMath.Clamp01(Threat + RandDelta(0.005));

        if (_rng.NextDouble() < 0.02)
            Threat = LifeMath.Clamp01(Threat + 0.2 + _rng.NextDouble() * 0.2);

        if (tick % 200 == 0)
            Novelty = LifeMath.Clamp01(Novelty + 0.1);

        var newEvents = new List<DeepBrain.Shared.BrainDtos.V4.WorldEventDto>(4);

        if (_rng.NextDouble() < 0.01)
            newEvents.Add(MakeEvent(tick, "threat_spike", 0.7, "sudden danger"));

        if (_rng.NextDouble() < 0.03)
            newEvents.Add(MakeEvent(tick, "novelty_opportunity", 0.5, "new pattern"));

        if (_rng.NextDouble() < (phase == "evening" ? 0.05 : 0.02))
            newEvents.Add(MakeEvent(tick, "social_ping", 0.5, "call from distance"));

        if (phase == "active" && sleepPressure > 0.5 && _rng.NextDouble() < 0.04)
            newEvents.Add(MakeEvent(tick, "fatigue_wave", 0.6, "energy dip"));

        if (Threat < 0.2 && _rng.NextDouble() < 0.02)
            newEvents.Add(MakeEvent(tick, "calm_window", 0.4, "safe window"));

        foreach (var ev in newEvents)
        {
            _events.Push(ev);
            ApplyEvent(ev);
        }

        return newEvents;
    }

    public void DampenNovelty(double strength)
    {
        Novelty = LifeMath.Clamp01(Novelty - 0.3 * strength);
    }

    private double RandDelta(double scale)
    {
        return (_rng.NextDouble() * 2.0 - 1.0) * scale;
    }

    private DeepBrain.Shared.BrainDtos.V4.WorldEventDto MakeEvent(long tick, string type, double severity, string payload)
    {
        return new DeepBrain.Shared.BrainDtos.V4.WorldEventDto(tick, type, LifeMath.Clamp01(severity), payload);
    }

    private void ApplyEvent(DeepBrain.Shared.BrainDtos.V4.WorldEventDto ev)
    {
        switch (ev.Type)
        {
            case "threat_spike":
                Threat = LifeMath.Clamp01(Threat + ev.Severity * 0.6);
                break;
            case "novelty_opportunity":
                Novelty = LifeMath.Clamp01(Novelty + ev.Severity * 0.5);
                break;
            case "social_ping":
                SocialPresence = LifeMath.Clamp01(SocialPresence + ev.Severity * 0.4);
                break;
            case "fatigue_wave":
                Noise = LifeMath.Clamp01(Noise + ev.Severity * 0.3);
                break;
            case "calm_window":
                Threat = LifeMath.Clamp01(Threat - ev.Severity * 0.4);
                break;
        }
    }
}
