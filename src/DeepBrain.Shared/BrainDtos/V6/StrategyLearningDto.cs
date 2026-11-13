using System.Text.Json.Serialization;
namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record StrategyChoiceDto([property:JsonRequired]string Id, [property:JsonRequired]string Title,
    [property:JsonRequired]string Context, [property:JsonRequired]string Reason,
    [property:JsonRequired]long Samples, [property:JsonRequired]double Confidence,
    [property:JsonRequired]double? ExpectedProgressPerAction)
{
    [JsonRequired] public double MinimumStrength { get; init; }
}
public sealed record StrategyAttemptDto(string Id, string PlanId, string GoalId, string Scenario,
    string StrategyId, string Title, string Context, string Outcome, string Diagnosis,
    int AwakeTicks, int Executions, int UnavailableTicks, int OtherActions, int InvalidActions,
    double? ExpectedProgressPerAction, double? ActualProgressPerAction, double EnergySpent, DateTimeOffset At);
public sealed record StrategyLearningDto(bool Enabled, long CompletedAttempts, long LearnedAttempts,
    int LearnedContexts, StrategyChoiceDto? Choice, IReadOnlyList<StrategyAttemptDto> RecentAttempts)
{
    public string? ActiveAttemptId { get; init; }
    public string? Phase { get; init; }
}
