namespace DeepBrain.Host.BrainLife.Ml;

public interface IBrainMlBackend : IAsyncDisposable
{
    bool IsAvailable { get; }
    string Kind { get; }
    bool IsConnected { get; }
    string? LastError { get; }
    double LastRttMs { get; }

    int InputDim { get; }
    int ActionCount { get; }
    int BufferSize { get; }
    int BufferCapacity { get; }
    double LastLoss { get; }
    double AvgLoss100 { get; }
    long TrainSteps { get; }
    double AvgQ { get; }

    Task<MlInferResult> InferAsync(float[] stateVec, float[] actionMask, MlConfig config, CancellationToken ct);
    Task<MlTrainResult> TrainAsync(Transition transition, MlConfig config, long tick, CancellationToken ct);
    bool TryLoad(string path, out int episodeId);
    void TrySave(string path, int episodeId);
    void Reset(MlConfig config);
    void ResetCounters();
    void UpdateConfig(MlConfig config);
    bool TryConnect();
    void Disconnect();
}
