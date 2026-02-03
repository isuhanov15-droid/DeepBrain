namespace DeepBrain.Shared.BrainDtos.V5;

public sealed record PainSourceDto(
    double Threat,
    double Fatigue,
    double Sleep,
    double Recovery
);
