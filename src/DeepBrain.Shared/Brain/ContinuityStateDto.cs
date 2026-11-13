namespace DeepBrain.Shared.Brain;

public sealed record ContinuityStateDto(bool Enabled, bool Restored, string? CheckpointId,
    DateTimeOffset? SavedAt, long? NextTick, string? RestoredCheckpointId, long? RestoredNextTick);
