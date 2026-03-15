using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record LoopInfoDto(
    [property: JsonPropertyName("isInLoop")] bool IsInLoop,
    [property: JsonPropertyName("observedCount")] int ObservedCount,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("strength")] double Strength,
    [property: JsonPropertyName("currentPenalty")] double CurrentPenalty,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("streak")] int Streak
);
