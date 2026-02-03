namespace DeepBrain.Shared.BrainDtos.V6;

public sealed record MlPolicyDto(
    bool Enabled,
    int InputDim,
    int ActionCount,
    double NetWeight,
    double Epsilon,
    int BufferSize,
    double LastLoss,
    long TrainSteps,
    double AvgReward200,
    double Entropy,
    string PolicySource
);
