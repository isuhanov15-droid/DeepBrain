namespace DeepBrain.Shared.Net;

public sealed record Envelope(
    string Type,
    string Id,
    long Ts,
    object? Payload
);
