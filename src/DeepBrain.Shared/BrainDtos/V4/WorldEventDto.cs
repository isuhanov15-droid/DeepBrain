namespace DeepBrain.Shared.BrainDtos.V4;

public sealed record WorldEventDto(
    long Tick,
    string Type,
    double Severity,
    string Payload,
    double AgeSeconds,
    double Salience
);
