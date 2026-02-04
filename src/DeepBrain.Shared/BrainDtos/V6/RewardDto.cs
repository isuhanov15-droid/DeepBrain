namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record RewardDto(
    double Homeostasis,
    double Explore,
    double Social,
    double LoopPenalty,
    double Total
);
