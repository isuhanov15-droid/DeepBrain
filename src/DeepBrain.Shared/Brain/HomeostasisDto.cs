namespace DeepBrain.Shared.Brain;

public sealed record HomeostasisDto(
    double Energy,
    double Fatigue,
    double Arousal,
    double Pain,
    double Safety
);
