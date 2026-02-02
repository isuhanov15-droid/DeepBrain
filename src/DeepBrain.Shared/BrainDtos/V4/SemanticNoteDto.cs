namespace DeepBrain.Shared.BrainDtos.V4;

public sealed record SemanticNoteDto(
    string Key,
    string BestAction,
    double Score,
    int Samples
);
