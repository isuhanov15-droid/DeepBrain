using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record RewardDto(
    [property: JsonPropertyName("homeostasis")] double Homeostasis,
    [property: JsonPropertyName("explore")] double Explore,
    [property: JsonPropertyName("social")] double Social,
    [property: JsonPropertyName("loopPenalty")] double LoopPenalty,
    [property: JsonPropertyName("invalidActionPenalty")] double InvalidActionPenalty,
    [property: JsonPropertyName("total")] double Total
);
