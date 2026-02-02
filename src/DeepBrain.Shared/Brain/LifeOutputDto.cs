namespace DeepBrain.Shared.Brain;

public sealed record LifeOutputDto(
    long Tick,
    DateTimeOffset Ts,
    string Message,
    string ActionName
);
