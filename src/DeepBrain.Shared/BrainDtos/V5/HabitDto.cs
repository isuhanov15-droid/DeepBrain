namespace DeepBrain.Shared.BrainDtos.V5;

public sealed record HabitDto(
    string Id,
    string CueKey,
    string RoutineAction,
    double Strength,
    int Uses,
    double AvgReward
);
