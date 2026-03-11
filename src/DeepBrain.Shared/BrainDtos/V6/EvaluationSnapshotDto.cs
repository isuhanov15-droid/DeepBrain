namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record EvaluationSnapshotDto(
    double MeanReward,
    double MedianReward,
    double SuccessRate,
    double AvgEpisodeLength,
    double LoopRate,
    double ActionDiversity,
    double CalmRatio,
    double AnxiousRatio,
    double CuriousRatio,
    double InvalidActionRate,
    double MaskFallbackRate,
    int EpisodeCount
);
