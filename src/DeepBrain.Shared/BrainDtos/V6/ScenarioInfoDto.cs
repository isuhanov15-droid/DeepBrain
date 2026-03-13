using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record ScenarioInfoDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("curriculumMode")] string CurriculumMode,
    [property: JsonPropertyName("index")] int Index
);
