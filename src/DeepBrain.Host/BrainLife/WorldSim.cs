namespace DeepBrain.Host.BrainLife;

public sealed class WorldSim
{
    private readonly Random _rng;
    private readonly WorldEventsQueue _events = new();
    private long _lastThreatTick = -1000;
    private string _lastMajorEvent = "none";

    public double Noise { get; private set; } = 0.2;
    public double Novelty { get; private set; } = 0.5;
    public double SocialPresence { get; private set; } = 0.4;
    public double Threat { get; private set; } = 0.1;
    public double CalmLevel { get; private set; } = 0.6;
    public double StressLevel { get; private set; } = 0.2;
    public double DriftRatePerSec { get; } = 0.02;
    public double ShockChanceBase { get; } = 0.01;

    public WorldEventsQueue Events => _events;
    public string LastMajorEvent => _lastMajorEvent;

    public WorldSim(int seed)
    {
        _rng = new Random(seed);
    }

    public IReadOnlyList<DeepBrain.Shared.BrainDtos.V4.WorldEventDto> Tick(long tick, string phase, double sleepPressure, bool isSleeping, double dtSeconds, double attachmentLevel)
    {
        _events.Tick(dtSeconds);

        UpdateClimate(dtSeconds);

        Noise = LifeMath.Clamp01(Noise + RandDelta(0.01));
        Novelty = LifeMath.Clamp01(Novelty + RandDelta(0.02));
        SocialPresence = LifeMath.Clamp01(SocialPresence + RandDelta(0.01));
        Threat = LifeMath.Clamp01(Threat + RandDelta(0.005));

        var threatCooldownSec = (tick - _lastThreatTick) * dtSeconds;
        var canThreat = threatCooldownSec > 20;
        var threatChance = ShockChanceBase * (0.3 + StressLevel);
        var newEvents = new List<DeepBrain.Shared.BrainDtos.V4.WorldEventDto>(4);
        if (canThreat && _rng.NextDouble() < threatChance)
        {
            var sev = 0.4 + _rng.NextDouble() * 0.4;
            var ev = MakeEvent(tick, "threat_spike", sev, "sudden danger");
            newEvents.Add(ev);
            _lastThreatTick = tick;
            _lastMajorEvent = ev.Type;
        }

        if (tick % 200 == 0)
            Novelty = LifeMath.Clamp01(Novelty + 0.1);

        if (_rng.NextDouble() < (0.03 + StressLevel * 0.05))
            newEvents.Add(MakeEvent(tick, "micro_threat", 0.25 + _rng.NextDouble() * 0.15, "minor risk"));

        var noveltyChance = 0.02 + Math.Max(0, CalmLevel - 0.6) * 0.06;
        if (_rng.NextDouble() < noveltyChance)
            newEvents.Add(MakeEvent(tick, "novelty_opportunity", 0.4 + _rng.NextDouble() * 0.2, "new pattern"));

        if ((phase == "evening" || phase == "night") && attachmentLevel > 0.2 && _rng.NextDouble() < 0.04)
            newEvents.Add(MakeEvent(tick, "social_ping", 0.4 + _rng.NextDouble() * 0.2, "call from distance"));

        if (phase == "active" && sleepPressure > 0.5 && _rng.NextDouble() < (0.02 + StressLevel * 0.03))
            newEvents.Add(MakeEvent(tick, "fatigue_wave", 0.45 + _rng.NextDouble() * 0.2, "energy dip"));

        if (CalmLevel > 0.6 && _rng.NextDouble() < (0.02 + CalmLevel * 0.03))
            newEvents.Add(MakeEvent(tick, "calm_window", 0.3 + _rng.NextDouble() * 0.2, "safe window"));

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
        var sev = LifeMath.Clamp01(severity);
        return new DeepBrain.Shared.BrainDtos.V4.WorldEventDto(tick, type, sev, payload, 0.0, sev, false);
    }

    private void ApplyEvent(DeepBrain.Shared.BrainDtos.V4.WorldEventDto ev)
    {
        switch (ev.Type)
        {
            case "threat_spike":
                Threat = LifeMath.Clamp01(Threat + ev.Severity * 0.6);
                StressLevel = LifeMath.Clamp01(StressLevel + 0.15);
                break;
            case "micro_threat":
                Threat = LifeMath.Clamp01(Threat + ev.Severity * 0.3);
                StressLevel = LifeMath.Clamp01(StressLevel + 0.05);
                break;
            case "novelty_opportunity":
                Novelty = LifeMath.Clamp01(Novelty + ev.Severity * 0.5);
                CalmLevel = LifeMath.Clamp01(CalmLevel + 0.05);
                break;
            case "social_ping":
                SocialPresence = LifeMath.Clamp01(SocialPresence + ev.Severity * 0.4);
                break;
            case "fatigue_wave":
                Noise = LifeMath.Clamp01(Noise + ev.Severity * 0.3);
                break;
            case "calm_window":
                Threat = LifeMath.Clamp01(Threat - ev.Severity * 0.4);
                CalmLevel = LifeMath.Clamp01(CalmLevel + 0.1);
                break;
        }

        if (ev.Type is "threat_spike" or "calm_window" or "novelty_opportunity")
            _lastMajorEvent = ev.Type;
    }

    private void UpdateClimate(double dtSeconds)
    {
        var calmDrift = DriftRatePerSec * dtSeconds * (0.6 - CalmLevel);
        var stressDrift = DriftRatePerSec * dtSeconds * (0.2 - StressLevel);
        CalmLevel = LifeMath.Clamp01(CalmLevel + calmDrift + RandDelta(0.01));
        StressLevel = LifeMath.Clamp01(StressLevel + stressDrift + RandDelta(0.01));
    }
}
