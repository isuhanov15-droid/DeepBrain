using System;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed record EpisodeReport(
    int EpisodeId,
    string ScenarioName,
    DateTimeOffset StartTs,
    DateTimeOffset EndTs,
    int Steps,
    string EndReason,
    double AvgReward,
    double TotalReward,
    RewardDto RewardBreakdownAvg,
    IReadOnlyDictionary<string, int> ActionHistogram,
    int LoopCount,
    double MaxLoopStrength,
    IReadOnlyDictionary<string, int> MoodDistribution,
    double AvgPain,
    double MaxPain,
    double AvgSafety,
    double AvgArousal,
    int SocialSignalsSent,
    int SelfTalkCount,
    int MaskFallbackCount,
    int InvalidActionCount,
    bool MlUsed,
    string BackendKind,
    double EpsilonUsed,
    ScenarioScore? ScenarioScore
);
