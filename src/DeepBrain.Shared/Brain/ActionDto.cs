namespace DeepBrain.Shared.Brain;

public sealed record ActionDto(
    string Kind,
    string Name,
    double Strength,
    string? Note
);
