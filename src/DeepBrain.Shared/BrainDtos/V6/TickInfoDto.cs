using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record TickInfoDto(
    [property: JsonPropertyName("globalTick")] long GlobalTick,
    [property: JsonPropertyName("episodeTick")] int EpisodeTick,
    [property: JsonPropertyName("deltaTime")] double DeltaTime
);
