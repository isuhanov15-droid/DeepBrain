using DeepBrain.Shared.Brain;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record ForecastStateDto(HomeostasisDto Body, double Novelty, double Agency,
    double Valence, double Arousal, bool SocialPending);
public sealed record ForecastStepDto(string Action, double Strength, ForecastStateDto Expected,
    double Confidence, double Uncertainty, string Source);
public sealed record ForecastOptionDto(string FirstAction, IReadOnlyList<ForecastStepDto> Steps,
    double ExpectedProgress, double EnergyCost, double Risk, double Confidence, double Score, string Status);
public sealed record ForecastCheckDto(string Id, string? GoalId, string Scenario, long StartedTick,
    string Status, int PlannedSteps, int CheckedSteps, double ExpectedProgress, double ActualProgress,
    double? MeanError, string FirstAction, double Confidence);
public sealed record ForesightDto(bool Enabled, string Status, string? RecommendedAction, string? SelectedAction,
    double AppliedBias, IReadOnlyList<ForecastOptionDto> Options, ForecastCheckDto? ActiveForecast,
    IReadOnlyList<ForecastCheckDto> RecentChecks, long Predictions, long CheckedSteps, long CompletedPlans,
    int LearnedModels, double? MeanError);
