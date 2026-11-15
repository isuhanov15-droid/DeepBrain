namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record SemanticAppraisalDto(int Version, string Source, string Model, string Kind,
    string Irony, string Target, double Confidence, bool Ambiguous);

public sealed record ConversationTraceDto(long Sequence, string RequestId, DateTimeOffset At,
    string Kind, string Label, string Excerpt, bool Historical)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SemanticAppraisalDto? Semantic { get; init; }
}
public sealed record ConversationExperienceDto(bool Enabled, long Experiences, long EmotionalExchanges,
    int Pending, long LastSequence, double ValenceInfluence, IReadOnlyDictionary<string,double> EmotionInfluence,
    IReadOnlyDictionary<string,long> Kinds, ConversationTraceDto? Last, ConversationTraceDto? LastEmotional);
