namespace DeepBrain.Shared.Brain;

using System.Text.Json.Serialization;
using DeepBrain.Shared.BrainDtos.V2;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V5;
using DeepBrain.Shared.BrainDtos.V6;

public sealed record LifeStateDto(
    [property: JsonPropertyName("tick")] long Tick,
    [property: JsonPropertyName("ts")] DateTimeOffset Ts,
    [property: JsonPropertyName("homeostasis")] HomeostasisDto Homeostasis,
    [property: JsonPropertyName("instincts")] InstinctsDto Instincts,
    [property: JsonPropertyName("affect")] AffectDto Affect,
    [property: JsonPropertyName("lastDecision")] string LastDecision,
    [property: JsonPropertyName("lastReward")] double LastReward,
    [property: JsonPropertyName("policy")] PolicyContextDto? Policy = null,
    [property: JsonPropertyName("dominantDrive")] string DominantDrive = "",
    [property: JsonPropertyName("moodInertia")] double MoodInertia = 0.0,
    [property: JsonPropertyName("circadian")] CircadianDto? Circadian = null,
    [property: JsonPropertyName("goals")] IReadOnlyList<GoalDto>? Goals = null,
    [property: JsonPropertyName("activePlan")] PlanDto? ActivePlan = null,
    [property: JsonPropertyName("attention")] AttentionDto? Attention = null,
    [property: JsonPropertyName("recentEvents")] IReadOnlyList<WorldEventDto>? RecentEvents = null,
    [property: JsonPropertyName("semanticNotesTop")] IReadOnlyList<SemanticNoteDto>? SemanticNotesTop = null,
    [property: JsonPropertyName("character")] CharacterStateDto? Character = null,
    [property: JsonPropertyName("climate")] ClimateDto? Climate = null,
    [property: JsonPropertyName("painSource")] PainSourceDto? PainSource = null,
    [property: JsonPropertyName("configVersion")] string? ConfigVersion = null,
    [property: JsonPropertyName("appraisal")] AppraisalDto? Appraisal = null,
    [property: JsonPropertyName("stats")] LifeStatsDto? Stats = null,
    [property: JsonPropertyName("ml")] MlPolicyDto? Ml = null,
    [property: JsonPropertyName("reward")] RewardDto? Reward = null,
    [property: JsonPropertyName("episode")] EpisodeInfoDto? Episode = null,
    [property: JsonPropertyName("scenario")] ScenarioInfoDto? Scenario = null,
    [property: JsonPropertyName("evaluation")] EvaluationSnapshotDto? Evaluation = null,
    [property: JsonPropertyName("decision")] DecisionDto? Decision = null,
    [property: JsonPropertyName("scenarioState")] ScenarioDto? ScenarioState = null,
    [property: JsonPropertyName("curriculum")] CurriculumStateDto? Curriculum = null,
    [property: JsonPropertyName("tickInfo")] TickInfoDto? TickInfo = null,
    [property: JsonPropertyName("loopInfo")] LoopInfoDto? LoopInfo = null
);
