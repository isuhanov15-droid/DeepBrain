namespace DeepBrain.Shared.Trace;

public sealed record TraceDto(
    long Tick,
    string Stage,
    object Data
);
