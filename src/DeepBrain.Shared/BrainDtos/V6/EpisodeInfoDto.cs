using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record EpisodeInfoDto(
    [property: JsonPropertyName("episodeId")] int EpisodeId,
    [property: JsonPropertyName("episodeTick")] int EpisodeTick,
    [property: JsonPropertyName("episodeLengthTicks")] int EpisodeLengthTicks,
    [property: JsonPropertyName("resetReason")] string ResetReason
);
