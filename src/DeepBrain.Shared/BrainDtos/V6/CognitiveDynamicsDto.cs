namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record EmotionSignalDto(string Id, double Intensity, string Evidence);
public sealed record WorkingMemoryItemDto(string Key, string Kind, string Text, double Salience, long AgeTicks);
public sealed record ActionExpectationDto(string Action, long Samples, double Confidence,
    double ExpectedUtility, double? Error, string Status);
public sealed record CognitiveDynamicsDto(bool Enabled, long Clock, string DominantEmotion,
    IReadOnlyList<EmotionSignalDto> Emotions, IReadOnlyList<WorkingMemoryItemDto> WorkingMemory,
    ActionExpectationDto? Expectation, int LearnedModels, long Observations, long GoalOutcomes,
    IReadOnlyDictionary<string, double> ActionBiases, bool Sleeping, string? LastError, DateTimeOffset? LastSavedAt);
