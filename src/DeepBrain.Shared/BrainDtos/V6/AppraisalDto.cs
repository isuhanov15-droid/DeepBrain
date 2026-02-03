namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record AppraisalDto(
    double Threat,
    double Novelty,
    double Social,
    double Fatigue
);
