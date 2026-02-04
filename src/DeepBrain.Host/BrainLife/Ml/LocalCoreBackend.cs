#if ML_CORE
using System.Linq;
using System.IO;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed class LocalCoreBackend : IBrainMlBackend
{
    private ExperienceBuffer _buffer;
    private PolicyNetAdapter _net;
    private OnlineTrainer _trainer;
    private readonly Random _rng;
    private readonly Action<string> _log;
    private readonly Queue<double> _lossWindow = new(100);
    private double _avgQ;

    public LocalCoreBackend(MlConfig config, Action<string> log)
    {
        _log = log;
        _rng = new Random(config.Seed);
        _buffer = new ExperienceBuffer(Math.Max(1024, config.BufferSize));
        _net = new PolicyNetAdapter(StateVectorizer.InputDim, ActionCatalog.Count, config.Seed, config.LearningRate);
        _trainer = new OnlineTrainer(_buffer, _net, _rng);
    }

    public bool IsAvailable => true;
    public string Kind => "local";
    public bool IsConnected => true;
    public string? LastError => null;
    public double LastRttMs => 0;
    public int InputDim => _net.InputDim;
    public int ActionCount => _net.ActionCount;
    public int BufferSize => _buffer.Count;
    public int BufferCapacity => _buffer.Capacity;
    public double LastLoss => _trainer.LastLoss;
    public double AvgLoss100 => AverageLoss();
    public long TrainSteps => _trainer.TrainSteps;
    public double AvgQ => _avgQ;

    public Task<MlInferResult> InferAsync(float[] stateVec, float[] actionMask, MlConfig config, CancellationToken ct)
    {
        var q = _net.PredictQ(stateVec);
        _avgQ = q.Length == 0 ? 0 : q.Average();
        var probs = PolicyNetAdapter.Softmax(q);
        var entropy = MlMath.ComputeEntropy(probs);
        var result = new MlInferResult(true, q, probs.Select(v => (float)v).ToArray(), entropy, _avgQ);
        return Task.FromResult(result);
    }

    public Task<MlTrainResult> TrainAsync(Transition transition, MlConfig config, long tick, CancellationToken ct)
    {
        _buffer.Add(transition);
        var result = _trainer.TryTrain(tick, config);
        if (result.IsNaN)
            return Task.FromResult(new MlTrainResult(false, double.NaN, 0, _trainer.TrainSteps, true));

        if (result.Trained)
            PushLoss(result.Loss);

        return Task.FromResult(new MlTrainResult(result.Trained, result.Loss, 0, _trainer.TrainSteps, false));
    }

    public bool TryLoad(string path, out int episodeId)
    {
        episodeId = 0;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (!File.Exists(path))
            return false;
        return _net.TryLoad(path);
    }

    public void TrySave(string path, int episodeId)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        _net.Save(path);
    }

    public void Reset(MlConfig config)
    {
        _buffer = new ExperienceBuffer(Math.Max(1024, config.BufferSize));
        _net.Reset(config.Seed, config.LearningRate);
        _trainer = new OnlineTrainer(_buffer, _net, _rng);
        _avgQ = 0;
        _lossWindow.Clear();
    }

    public void ResetCounters()
    {
        _lossWindow.Clear();
    }

    public void UpdateConfig(MlConfig config)
    {
        if (config.BufferSize > 0 && config.BufferSize != _buffer.Capacity)
            _buffer = new ExperienceBuffer(Math.Max(1024, config.BufferSize));
    }

    public bool TryConnect() => true;
    public void Disconnect() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
}
#endif
