using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record DecisionDto(
    [property: JsonPropertyName("actionName")] string ActionName,
    [property: JsonPropertyName("actionKind")] string ActionKind,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("maskStatus")] string MaskStatus,
    [property: JsonPropertyName("policySource")] string PolicySource
);
