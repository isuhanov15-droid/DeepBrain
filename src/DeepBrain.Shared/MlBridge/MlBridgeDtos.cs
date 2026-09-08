using System.Text.Json.Serialization;

namespace DeepBrain.Shared.MlBridge;

public sealed record MlInferRequest(
    float[] State,
    [property: JsonConverter(typeof(FlexibleBoolArrayConverter))]
    bool[]? ActionMask,
    int InputDim,
    int ActionCount
)
{
    public int? Seed { get; init; }
    public double? LearningRate { get; init; }
}

public sealed record MlInferResponse(
    int ActionIndex,
    double[] QValues,
    float[]? Probabilities,
    double Entropy,
    double AvgQ
)
{
    public string? ServerInstance { get; init; }
    public bool Ok { get; init; } = true;
    public string? Reason { get; init; }
}

public sealed record MlTransitionDto(
    float[] State,
    int ActionIndex,
    float Reward,
    float[] NextState,
    bool Done,
    [property: JsonConverter(typeof(FlexibleBoolArrayConverter))]
    bool[]? NextActionMask
);

public sealed record MlTrainConfigDto(
    int BufferSize,
    int BatchSize,
    int TrainStepsPerBatch,
    int TrainEveryTicks,
    int TargetUpdateTicks,
    double Gamma,
    double GradClip,
    int Seed
);

public sealed record MlTrainRequest(
    long Tick,
    MlTransitionDto Transition,
    MlTrainConfigDto Config,
    int InputDim,
    int ActionCount
) { public string? ExpectedInstanceId { get; init; } }

public sealed record MlTrainResponse(
    bool Ok,
    bool Trained,
    double Loss,
    double GradNorm,
    long TrainSteps,
    int BufferSize,
    string? Reason
);

public sealed record MlCheckpointRequest(string Path) { public string? ExpectedInstanceId { get; init; } }

public sealed record MlCheckpointResponse(bool Ok, string? Meta);

public sealed record MlPingRequest(string? Client);

public sealed record MlPingResponse(string Status);

public sealed record MlResetRequest(int Seed, double LearningRate);
