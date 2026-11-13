using DeepBrain.Shared.BrainDtos.V3;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record GoalOutcomeDto(string PlanId, string GoalId, string GoalTitle, string Status,
    string Reason, string Evidence, int AwakeTicks, int Replans, double Progress, DateTimeOffset At);

public sealed record ExecutiveStateDto(bool Enabled, long AwakeClock, PlanDto? Plan,
    long Succeeded, long Failed, long Interrupted, IReadOnlyList<GoalOutcomeDto> RecentOutcomes,
    string? LastError, DateTimeOffset? LastSavedAt, string? PreferredAction, double AppliedBias,
    string BiasStatus, bool RevalidatedAfterRestart)
{
    public string? SavedScenario { get; init; }
    public string ScenarioResumeStatus { get; init; } = "not_attempted";
    public StrategyLearningDto? StrategyLearning { get; init; }
}
