#if ML_CORE
namespace DeepBrain.Host.BrainLife.Ml;

public sealed class OnlineTrainer
{
    private readonly ExperienceBuffer _buffer;
    private readonly PolicyNetAdapter _net;
    private readonly Random _rng;

    public double LastLoss { get; private set; }
    public long TrainSteps { get; private set; }

    public OnlineTrainer(ExperienceBuffer buffer, PolicyNetAdapter net, Random rng)
    {
        _buffer = buffer;
        _net = net;
        _rng = rng;
    }

    public TrainOutcome TryTrain(long tick, MlConfig config)
    {
        if (config.TrainEveryTicks <= 0) return TrainOutcome.None;
        if (tick % config.TrainEveryTicks != 0) return TrainOutcome.None;
        if (_buffer.Count < config.BatchSize || config.BatchSize <= 0) return TrainOutcome.None;

        var steps = Math.Max(1, config.TrainStepsPerBatch);
        var lossSum = 0.0;
        var didTrain = false;
        for (var i = 0; i < steps; i++)
        {
            var batch = _buffer.SampleBatch(config.BatchSize, _rng);
            if (batch.Count == 0) break;
            var loss = _net.TrainBatch(batch, config.Gamma, config.GradClip);
            if (double.IsNaN(loss) || double.IsInfinity(loss))
                return TrainOutcome.NaN;
            lossSum += loss;
            TrainSteps++;
            didTrain = true;
        }

        if (didTrain)
            LastLoss = lossSum / steps;

        return didTrain ? new TrainOutcome(true, LastLoss) : TrainOutcome.None;
    }
}

public readonly record struct TrainOutcome(bool Trained, double Loss)
{
    public static readonly TrainOutcome None = new(false, 0);
    public static readonly TrainOutcome NaN = new(false, double.NaN);
    public bool IsNaN => double.IsNaN(Loss) || double.IsInfinity(Loss);
}
#endif
