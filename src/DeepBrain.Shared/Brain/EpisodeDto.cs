namespace DeepBrain.Shared.Brain;

public sealed record EpisodeDto(
    long Tick,
    HomeostasisDto BeforeHomeostasis,
    HomeostasisDto AfterHomeostasis,
    ActionDto Action,
    double Reward,
    DateTimeOffset Ts
);
