using DeepBrain.Shared.Input;

namespace DeepBrain.Host.Brain.Perception;

public sealed record Percept(DateTimeOffset TimeUtc, long Tick, BrainInputDto Input);
