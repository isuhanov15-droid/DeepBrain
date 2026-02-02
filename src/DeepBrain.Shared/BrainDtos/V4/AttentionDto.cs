namespace DeepBrain.Shared.BrainDtos.V4;

public sealed record AttentionDto(
    string Focus1,
    string? Focus2,
    double Intensity,
    string Reason
);
