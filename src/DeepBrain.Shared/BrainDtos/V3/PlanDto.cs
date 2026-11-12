namespace DeepBrain.Shared.BrainDtos.V3;

public sealed record PlanDto(
    string Strategy,
    int RemainingTicks,
    string GoalId,
    string Rationale
)
{
    public string? PlanId { get; init; }
    public string Status { get; init; } = "active";
    public string Stage { get; init; } = "execute";
    public string? GoalTitle { get; init; }
    public string? SuccessCriterion { get; init; }
    public double Progress { get; init; }
    public int AgeTicks { get; init; }
    public int Replans { get; init; }
    public string? Evidence { get; init; }
    public string? PreferredAction { get; init; }
}
