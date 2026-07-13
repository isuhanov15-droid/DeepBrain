using System;
using System.Text.Json.Serialization;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed record EpisodeReport(
    [property: JsonPropertyName("episodeId")] int EpisodeId,
    [property: JsonPropertyName("scenario")] string ScenarioName,
    [property: JsonPropertyName("startTs")] DateTimeOffset StartTs,
    [property: JsonPropertyName("endTs")] DateTimeOffset EndTs,
    [property: JsonPropertyName("ticks")] int Steps,
    [property: JsonPropertyName("endReason")] string EndReason,
    [property: JsonPropertyName("avgReward")] double AvgReward,
    [property: JsonPropertyName("totalReward")] double TotalReward,
    [property: JsonPropertyName("rewardBreakdownAvg")] RewardDto RewardBreakdownAvg,
    [property: JsonPropertyName("actionHistogram")] IReadOnlyDictionary<string, int> ActionHistogram,
    [property: JsonPropertyName("loopEvents")] int LoopCount,
    [property: JsonPropertyName("maxLoopStrength")] double MaxLoopStrength,
    [property: JsonPropertyName("moodDistribution")] IReadOnlyDictionary<string, int> MoodDistribution,
    [property: JsonPropertyName("avgPain")] double AvgPain,
    [property: JsonPropertyName("maxPain")] double MaxPain,
    [property: JsonPropertyName("avgSafety")] double AvgSafety,
    [property: JsonPropertyName("avgArousal")] double AvgArousal,
    [property: JsonPropertyName("socialSignalsSent")] int SocialSignalsSent,
    [property: JsonPropertyName("selfTalkCount")] int SelfTalkCount,
    [property: JsonPropertyName("maskFallbackCount")] int MaskFallbackCount,
    [property: JsonPropertyName("invalidActions")] int InvalidActionCount,
    [property: JsonPropertyName("mlUsed")] bool MlUsed,
    [property: JsonPropertyName("backendKind")] string BackendKind,
    [property: JsonPropertyName("epsilonUsed")] double EpsilonUsed,
    [property: JsonPropertyName("scenarioScore")] ScenarioScore? ScenarioScore
)
{
    // Optional v2 telemetry. Older reports deserialize with an empty map and
    // older consumers can ignore the new JSON property.
    [JsonPropertyName("actionRewardAverages")]
    public IReadOnlyDictionary<string, double> ActionRewardAverages { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
}
