namespace DeepBrain.Shared.BrainDtos.V3;

public sealed record PlanDto(
    string Strategy,
    int RemainingTicks,
    string GoalId,
    string Rationale
);
