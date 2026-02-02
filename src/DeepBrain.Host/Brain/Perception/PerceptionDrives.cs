namespace DeepBrain.Host.Brain.Perception;

public sealed record PerceptionDrives(
    float Threat,
    float Fatigue,
    float GoalPull
);
