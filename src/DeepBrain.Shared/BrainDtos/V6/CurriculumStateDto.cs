using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record CurriculumStateDto(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("index")] int Index
);
