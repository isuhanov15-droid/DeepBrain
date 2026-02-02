namespace DeepBrain.Shared.BrainDtos.V2;

public sealed record PolicyContextDto(
    string Strategy,
    string Reason,
    int LoopCount,
    double LoopPenalty,
    string LastAction,
    int SameActionStreak,
    double AvgRewardShort
);
