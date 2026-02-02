namespace DeepBrain.Shared.Brain;

public sealed record OutcomeDto(
    ActionDto Action,
    double Reward,
    string? Message
);
