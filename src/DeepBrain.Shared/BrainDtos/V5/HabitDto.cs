namespace DeepBrain.Shared.BrainDtos.V5;

public sealed record HabitDto(
    string Id,
    string CueKey,
    string RoutineAction,
    double Strength,
    int Uses,
    double AvgReward
)
{
    public long CueExposures { get; init; }
    public long RoutineExecutions { get; init; }
    public long PositiveReinforcements { get; init; }
    public long NegativeReinforcements { get; init; }
    public double CueRewardBaseline { get; init; }
    public double RoutineRewardAverage { get; init; }
    public double AdvantageAverage { get; init; }
    public long LastCueTick { get; init; }
    public long LastExecutionTick { get; init; }
    public bool ImportedLegacy { get; init; }
}
