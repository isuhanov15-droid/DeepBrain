using System.Text.Json.Serialization;

namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record DecisionDto(
    [property: JsonPropertyName("actionName")] string ActionName,
    [property: JsonPropertyName("actionKind")] string ActionKind,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("maskStatus")] string MaskStatus,
    [property: JsonPropertyName("policySource")] string PolicySource
)
{
    [JsonPropertyName("cortexAdvice")]
    public CortexAdviceDto? CortexAdvice { get; init; }
    [JsonPropertyName("candidateActions")]
    public IReadOnlyList<string> CandidateActions { get; init; } = Array.Empty<string>();
}

public sealed record CortexAdviceDto(
    string Status,
    string SuggestedAction,
    double Confidence,
    double ScoreBias,
    long? FrameId,
    long? FrameTick,
    long? AgeTicks,
    string? SelectedAction = null,
    bool MatchesSelection = false
);
