namespace DeepBrain.Shared.Brain;

public sealed record BrainStateDto(
    long Tick,
    long UptimeMs,
    string Mode,
    string LastDecision
);
