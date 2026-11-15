namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record ConversationTraceDto(long Sequence, string RequestId, DateTimeOffset At,
    string Kind, string Label, string Excerpt, bool Historical);
public sealed record ConversationExperienceDto(bool Enabled, long Experiences, long EmotionalExchanges,
    int Pending, long LastSequence, double ValenceInfluence, IReadOnlyDictionary<string,double> EmotionInfluence,
    IReadOnlyDictionary<string,long> Kinds, ConversationTraceDto? Last, ConversationTraceDto? LastEmotional);
