namespace DeepBrain.Shared.Brain;

using DeepBrain.Shared.BrainDtos.V2;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;

public sealed record LifeStateDto(
    long Tick,
    DateTimeOffset Ts,
    HomeostasisDto Homeostasis,
    InstinctsDto Instincts,
    AffectDto Affect,
    string LastDecision,
    double LastReward,
    PolicyContextDto? Policy = null,
    string DominantDrive = "",
    double MoodInertia = 0.0,
    CircadianDto? Circadian = null,
    IReadOnlyList<GoalDto>? Goals = null,
    PlanDto? ActivePlan = null,
    AttentionDto? Attention = null,
    IReadOnlyList<WorldEventDto>? RecentEvents = null,
    IReadOnlyList<SemanticNoteDto>? SemanticNotesTop = null
);
