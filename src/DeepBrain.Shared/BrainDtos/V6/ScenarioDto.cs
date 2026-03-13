using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record ScenarioDto(
    [property: JsonPropertyName("name")] string Name
);
