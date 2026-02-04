namespace DeepBrain.Host.BrainLife.Ml;

public sealed class StubBackend : IBrainMlBackend
{
    public bool IsAvailable => false;
    public string Kind => "stub";
    public bool IsConnected => false;
    public string? LastError => null;
    public double LastRttMs => 0;
    public int InputDim => 0;
    public int ActionCount => 0;
    public int BufferSize => 0;
    public int BufferCapacity => 0;
    public double LastLoss => 0;
    public double AvgLoss100 => 0;
    public long TrainSteps => 0;
    public double AvgQ => 0;

    public Task<MlInferResult> InferAsync(float[] stateVec, float[] actionMask, MlConfig config, CancellationToken ct)
        => Task.FromResult(new MlInferResult(false, Array.Empty<double>(), null, 0, 0));

    public Task<MlTrainResult> TrainAsync(Transition transition, MlConfig config, long tick, CancellationToken ct)
        => Task.FromResult(new MlTrainResult(false, 0, 0, 0, false));

    public bool TryLoad(string path, out int episodeId)
    {
        episodeId = 0;
        return false;
    }

    public void TrySave(string path, int episodeId) { }
    public void Reset(MlConfig config) { }
    public void ResetCounters() { }
    public void UpdateConfig(MlConfig config) { }
    public bool TryConnect() => false;
    public void Disconnect() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
