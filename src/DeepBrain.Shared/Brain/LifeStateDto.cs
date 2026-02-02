namespace DeepBrain.Shared.Brain;

public sealed record LifeStateDto(
    long Tick,
    DateTimeOffset Ts,
    HomeostasisDto Homeostasis,
    InstinctsDto Instincts,
    AffectDto Affect,
    string LastDecision,
    double LastReward
);
