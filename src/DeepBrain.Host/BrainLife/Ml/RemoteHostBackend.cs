using System.Linq;
using DeepBrain.Shared.MlBridge;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed class RemoteHostBackend : IBrainMlBackend
{
    private readonly Action<string> _log;
    private MlRpcClient _client;
    private readonly Queue<double> _lossWindow = new(100);
    private DateTime _lastErrorLog = DateTime.MinValue;
    private double _avgQ;
    private double _lastLoss;
    private long _trainSteps;
    private int _bufferSize;

    public RemoteHostBackend(MlConfig config, Action<string> log)
    {
        _log = log;
        _client = new MlRpcClient(config.Remote, log);
    }

    public bool IsAvailable => true;
    public string Kind => "remote";
    public bool IsConnected => _client.IsConnected;
    public string? LastError => _client.LastError;
    public double LastRttMs => _client.LastRttMs;
    public int InputDim => StateVectorizer.InputDim;
    public int ActionCount => ActionCatalog.Count;
    public int BufferSize => _bufferSize;
    public int BufferCapacity { get; private set; }
    public double LastLoss => _lastLoss;
    public double AvgLoss100 => AverageLoss();
    public long TrainSteps => _trainSteps;
    public double AvgQ => _avgQ;

    public async Task<MlInferResult> InferAsync(float[] stateVec, float[] actionMask, MlConfig config, CancellationToken ct)
    {
        BufferCapacity = Math.Max(1024, config.BufferSize);
        var req = new MlInferRequest(
            State: stateVec,
            ActionMask: config.ActionMasking ? ToBoolMask(actionMask) : null,
            InputDim: StateVectorizer.InputDim,
            ActionCount: ActionCatalog.Count
        );

        var resp = await _client.CallAsync<MlInferRequest, MlInferResponse>("ml.infer", req, ct);
        if (resp == null)
        {
            RateLimitedLog($"warn: ml.infer failed: {_client.LastError}");
            return new MlInferResult(false, Array.Empty<double>(), null, 0, 0);
        }

        _avgQ = resp.AvgQ;
        return new MlInferResult(true, resp.QValues, resp.Probabilities, resp.Entropy, resp.AvgQ);
    }

    public async Task<MlTrainResult> TrainAsync(Transition transition, MlConfig config, long tick, CancellationToken ct)
    {
        BufferCapacity = Math.Max(1024, config.BufferSize);
        var req = new MlTrainRequest(
            Tick: tick,
            Transition: new MlTransitionDto(
                transition.State,
                transition.Action,
                transition.Reward,
                transition.NextState,
                transition.Done,
                ToBoolMask(transition.ActionMask)
            ),
            Config: new MlTrainConfigDto(
                BufferSize: BufferCapacity,
                BatchSize: config.BatchSize,
                TrainStepsPerBatch: config.TrainStepsPerBatch,
                TrainEveryTicks: config.TrainEveryTicks,
                TargetUpdateTicks: config.TargetUpdateTicks,
                Gamma: config.Gamma,
                GradClip: config.GradClip,
                Seed: config.Seed
            ),
            InputDim: StateVectorizer.InputDim,
            ActionCount: ActionCatalog.Count
        );

        var resp = await _client.CallAsync<MlTrainRequest, MlTrainResponse>("ml.train", req, ct);
        if (resp == null)
        {
            RateLimitedLog($"warn: ml.train failed: {_client.LastError}");
            return new MlTrainResult(false, double.NaN, 0, _trainSteps, true);
        }

        _bufferSize = resp.BufferSize;
        _trainSteps = resp.TrainSteps;
        _lastLoss = resp.Loss;
        if (resp.Trained)
            PushLoss(resp.Loss);
        var isNaN = double.IsNaN(resp.Loss) || double.IsInfinity(resp.Loss);
        return new MlTrainResult(resp.Trained, resp.Loss, resp.GradNorm, resp.TrainSteps, isNaN);
    }

    public bool TryLoad(string path, out int episodeId)
    {
        episodeId = 0;
        var req = new MlCheckpointRequest(path);
        var resp = _client.CallAsync<MlCheckpointRequest, MlCheckpointResponse>("ml.checkpoint.load", req, CancellationToken.None).GetAwaiter().GetResult();
        return resp != null && resp.Ok;
    }

    public void TrySave(string path, int episodeId)
    {
        var req = new MlCheckpointRequest(path);
        _ = _client.CallAsync<MlCheckpointRequest, MlCheckpointResponse>("ml.checkpoint.save", req, CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Reset(MlConfig config)
    {
        _lossWindow.Clear();
        _lastLoss = 0;
        _avgQ = 0;
        _trainSteps = 0;
        _bufferSize = 0;
        BufferCapacity = Math.Max(1024, config.BufferSize);
        _client.UpdateConfig(config.Remote);
    }

    public void ResetCounters()
    {
        _lossWindow.Clear();
    }

    public void UpdateConfig(MlConfig config)
    {
        BufferCapacity = Math.Max(1024, config.BufferSize);
        _client.UpdateConfig(config.Remote);
    }

    public bool TryConnect() => _client.TryConnect();
    public void Disconnect() => _client.Disconnect();
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private void PushLoss(double loss)
    {
        if (_lossWindow.Count >= 100) _lossWindow.Dequeue();
        _lossWindow.Enqueue(loss);
    }

    private double AverageLoss()
    {
        if (_lossWindow.Count == 0) return 0;
        return _lossWindow.Average();
    }

    private void RateLimitedLog(string message)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastErrorLog).TotalSeconds < 5)
            return;
        _lastErrorLog = now;
        _log(message);
    }

    private static bool[]? ToBoolMask(float[]? mask)
    {
        if (mask == null) return null;
        var arr = new bool[mask.Length];
        for (var i = 0; i < mask.Length; i++)
            arr[i] = mask[i] > 0.5f;
        return arr;
    }
}
