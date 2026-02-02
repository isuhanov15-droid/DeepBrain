namespace DeepBrain.Shared.Brain;

public sealed record AffectDto(
    string Mood,
    double Valence,
    double Arousal
);
