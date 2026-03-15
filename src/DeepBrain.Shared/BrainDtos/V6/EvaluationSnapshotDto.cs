using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record EvaluationSnapshotDto(
    [property: JsonPropertyName("avgReward")] double AvgReward,
    [property: JsonPropertyName("medianReward")] double MedianReward,
    [property: JsonPropertyName("successRate")] double SuccessRate,
    [property: JsonPropertyName("avgEpisodeLength")] double AvgEpisodeLength,
    [property: JsonPropertyName("loopRate")] double LoopRate,
    [property: JsonPropertyName("actionDiversity")] double ActionDiversity,
    [property: JsonPropertyName("calmRatio")] double CalmRatio,
    [property: JsonPropertyName("anxiousRatio")] double AnxiousRatio,
    [property: JsonPropertyName("curiousRatio")] double CuriousRatio,
    [property: JsonPropertyName("invalidActionRate")] double InvalidActionRate,
    [property: JsonPropertyName("maskFallbackRate")] double MaskFallbackRate,
    [property: JsonPropertyName("episodeCount")] int EpisodeCount,
    [property: JsonPropertyName("calmCount")] int CalmCount,
    [property: JsonPropertyName("anxiousCount")] int AnxiousCount,
    [property: JsonPropertyName("curiousCount")] int CuriousCount,
    [property: JsonPropertyName("loopCount")] int LoopCount
);
