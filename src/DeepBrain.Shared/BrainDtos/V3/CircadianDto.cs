namespace DeepBrain.Shared.BrainDtos.V3;

public sealed record CircadianDto(
    string Phase,
    double TimeOfDay,
    bool IsSleeping,
    double SleepPressure
);
