using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.Cognition;

public static class CognitiveContract
{
    public const string Version = "deepbrain.cortex.v2";
    public const int InterpretationMaxLength = 200;
    public const int InnerSpeechMaxLength = 140;
    public const int MemoryQuestionMaxLength = 160;

    public static readonly IReadOnlySet<string> InsightProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "interpretation",
        "inner_speech",
        "memory_question",
        "risk_level",
        "confidence"
    };
}

public sealed record CognitiveFrame(
    string ContractVersion,
    long FrameId,
    long Tick,
    DateTimeOffset Timestamp,
    string Scenario,
    string Mood,
    double Safety,
    double Pain,
    double Arousal,
    string DominantDrive,
    string LastDecision,
    double LastReward,
    IReadOnlyList<string> RecentEvents,
    IReadOnlyList<string> MemoryContext
)
{
    public double Energy { get; init; }
    public double Fatigue { get; init; }
    public double ExplorationNeed { get; init; }
    public double AttachmentNeed { get; init; }
    public double AgencyNeed { get; init; }
    public string AttentionFocus { get; init; } = "";
    public string ActiveHabitId { get; init; } = "";
    public double HabitInfluence { get; init; }
    public string PreviousInnerSpeech { get; init; } = "";
}

public sealed record CognitiveInsight(
    [property: JsonPropertyName("interpretation")] string Interpretation,
    [property: JsonPropertyName("inner_speech")] string InnerSpeech,
    [property: JsonPropertyName("memory_question")] string MemoryQuestion,
    [property: JsonPropertyName("risk_level")] string RiskLevel,
    [property: JsonPropertyName("confidence")] double Confidence)
{
    public static bool TryParseStrict(string json, out CognitiveInsight? insight, out string? error)
    {
        insight = null;
        error = null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "LLM response root must be an object";
                return false;
            }

            var names = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            if (!names.SetEquals(CognitiveContract.InsightProperties))
            {
                var missing = CognitiveContract.InsightProperties.Except(names).OrderBy(value => value);
                var unknown = names.Except(CognitiveContract.InsightProperties).OrderBy(value => value);
                error = $"LLM contract mismatch; missing=[{string.Join(",", missing)}] unknown=[{string.Join(",", unknown)}]";
                return false;
            }

            if (!TryGetBoundedString(root, "interpretation", 1, CognitiveContract.InterpretationMaxLength, out var interpretation, out error) ||
                !TryGetBoundedString(root, "inner_speech", 0, CognitiveContract.InnerSpeechMaxLength, out var innerSpeech, out error) ||
                !TryGetBoundedString(root, "memory_question", 0, CognitiveContract.MemoryQuestionMaxLength, out var memoryQuestion, out error) ||
                !TryGetBoundedString(root, "risk_level", 1, 16, out var riskLevel, out error))
                return false;

            if (riskLevel is not ("low" or "medium" or "high"))
            {
                error = "risk_level must be low, medium, or high";
                return false;
            }

            var confidenceElement = root.GetProperty("confidence");
            if (confidenceElement.ValueKind != JsonValueKind.Number ||
                !confidenceElement.TryGetDouble(out var confidence) ||
                !double.IsFinite(confidence) || confidence is < 0 or > 1)
            {
                error = "confidence must be a finite number in [0,1]";
                return false;
            }

            insight = new CognitiveInsight(interpretation!, innerSpeech!, memoryQuestion!, riskLevel!, confidence);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"invalid LLM JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetBoundedString(
        JsonElement root,
        string name,
        int minLength,
        int maxLength,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        var element = root.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"{name} must be a string";
            return false;
        }

        value = element.GetString()?.Trim() ?? "";
        if (value.Length < minLength || value.Length > maxLength)
        {
            error = $"{name} length must be in [{minLength},{maxLength}]";
            return false;
        }

        return true;
    }
}

public sealed record CognitiveInsightEnvelope(
    string ContractVersion,
    long FrameId,
    long FrameTick,
    long AcceptedAtTick,
    DateTimeOffset CreatedAt,
    string Provider,
    string Model,
    string Reason,
    TimeSpan Latency,
    CognitiveInsight Insight,
    LlmGenerationMetrics? Metrics = null
);

public sealed record LlmGenerationMetrics(
    int RequestBytes,
    string? DoneReason,
    long? PromptTokens,
    long? OutputTokens,
    double? TotalSeconds,
    double? LoadSeconds,
    double? PromptEvaluationSeconds,
    double? OutputEvaluationSeconds,
    int PromptCharacters = 0,
    int DataCharacters = 0)
{
    public double? PromptTokensPerSecond => Rate(PromptTokens, PromptEvaluationSeconds);
    public double? OutputTokensPerSecond => Rate(OutputTokens, OutputEvaluationSeconds);

    private static double? Rate(long? tokens, double? seconds)
    {
        if (!tokens.HasValue || !seconds.HasValue || tokens.Value < 0 || seconds.Value <= 0)
            return null;
        return tokens.Value / seconds.Value;
    }
}

public sealed record LlmCortexResponse(
    bool Success,
    CognitiveInsight? Insight,
    TimeSpan Latency,
    string? Error,
    LlmGenerationMetrics? Metrics = null
);

public sealed record LlmCortexStatus(
    bool Enabled,
    string Provider,
    string Model,
    bool InFlight,
    long LatestStateTick,
    long LastQueuedFrameId,
    long Completed,
    long Failed,
    long DroppedAsStale,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    long InFlightFrameTick = 0,
    long InFlightAgeTicks = 0,
    double? LastLatencySeconds = null,
    long LastFrameAgeTicks = 0,
    LlmGenerationMetrics? LastMetrics = null,
    DateTimeOffset? InFlightStartedAt = null,
    double InFlightSeconds = 0,
    long DroppedLowConfidence = 0,
    long LastAutoQueuedTick = 0,
    CognitiveJournalStatus? Journal = null
);
