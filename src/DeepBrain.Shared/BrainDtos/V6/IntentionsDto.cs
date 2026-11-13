namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record DeferredIntentionDto(string PlanId, string GoalId, string GoalTitle,
    double Progress, int AgeTicks, string Reason, string? WaitingForGoalId, string ResumeCondition, long DeferredAwakeTicks);

public sealed record IntentionTransitionDto(long Revision, string PlanId, string GoalId,
    string Kind, string Reason, string? RelatedGoalId, string Evidence, DateTimeOffset At);

public sealed record IntentionsDto(bool Enabled, string? MainPlanId, string? MainGoalTitle,
    string? ParentPlanId, long Suspensions, long Resumptions, long Cancellations,
    IReadOnlyList<DeferredIntentionDto> Deferred, IReadOnlyList<IntentionTransitionDto> RecentTransitions);
