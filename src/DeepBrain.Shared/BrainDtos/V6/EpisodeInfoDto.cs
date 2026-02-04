namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record EpisodeInfoDto(
    int EpisodeId,
    int EpisodeTick,
    int EpisodeLengthTicks,
    string ResetReason
);
