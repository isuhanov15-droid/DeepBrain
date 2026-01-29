namespace DeepBrain.Shared.Input;

public sealed record BrainInputDto(
    float Stress,
    float Energy,
    float Focus,
    string Goal,
    string Command
);
