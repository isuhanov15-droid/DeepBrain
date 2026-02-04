namespace DeepBrain.Host.BrainLife.Ml;

public readonly record struct MlInferResult(
    bool Ok,
    double[] QValues,
    float[]? Probabilities,
    double Entropy,
    double AvgQ
);

public readonly record struct MlTrainResult(
    bool Trained,
    double Loss,
    double GradNorm,
    long TrainSteps,
    bool IsNaN
);
