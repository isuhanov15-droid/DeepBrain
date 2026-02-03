#if ML_CORE
using ML.Core;
using ML.Core.Layers;
using ML.Core.Optimizers;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed class PolicyNetAdapter
{
    private Network _net;
    private AdamOptimizer _optimizer;
    private readonly int _inputDim;
    private readonly int _actionCount;
    private int _seed;
    private double _lr;

    public PolicyNetAdapter(int inputDim, int actionCount, int seed, double learningRate)
    {
        _inputDim = inputDim;
        _actionCount = actionCount;
        _seed = seed;
        _lr = learningRate;
        (_net, _optimizer) = BuildNet(seed, learningRate);
    }

    public int InputDim => _inputDim;
    public int ActionCount => _actionCount;

    public double[] PredictQ(float[] state)
    {
        var input = ToDouble(state);
        var q = _net.Forward(input, training: false);
        if (q.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            return new double[_actionCount];
        return q;
    }

    public float[] PredictProbs(float[] state)
    {
        var q = PredictQ(state);
        var probs = Softmax(q);
        var result = new float[probs.Length];
        for (var i = 0; i < probs.Length; i++)
            result[i] = (float)probs[i];
        return result;
    }

    public double TrainBatch(IReadOnlyList<Transition> batch, double gamma, double gradClip)
    {
        if (batch.Count == 0) return 0;

        _optimizer.ZeroGrad(_net.Parameters());
        double lossSum = 0;

        foreach (var t in batch)
        {
            var q = _net.Forward(ToDouble(t.State), training: true);
            var nextQ = _net.Forward(ToDouble(t.NextState), training: false);
            if (q.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || nextQ.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                return double.NaN;
            var maxNext = nextQ.Length == 0 ? 0 : nextQ.Max();
            var target = t.Reward + (t.Done ? 0.0 : gamma * maxNext);
            var diff = q[t.Action] - target;
            lossSum += diff * diff;

            var grad = new double[_actionCount];
            grad[t.Action] = 2.0 * diff;
            _net.Backward(grad);
        }

        var scale = 1.0 / batch.Count;
        ScaleGrads(scale);
        ClipGrads(gradClip);

        if (double.IsNaN(lossSum) || double.IsInfinity(lossSum))
        {
            _optimizer.ZeroGrad(_net.Parameters());
            return double.NaN;
        }

        _optimizer.Step(_net.Parameters());
        return lossSum / batch.Count;
    }

    public void Reset(int seed, double learningRate)
    {
        _seed = seed;
        _lr = learningRate;
        (_net, _optimizer) = BuildNet(seed, learningRate);
    }

    public bool TryLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        _net = ML.Core.Serialization.ModelStore.LoadFromFile(path);
        _optimizer = new AdamOptimizer(_lr);
        return true;
    }

    public void Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        ML.Core.Serialization.ModelStore.SaveToFile(path, _net);
    }

    private (Network net, AdamOptimizer opt) BuildNet(int seed, double lr)
    {
        var net = new Network();
        var s1 = seed;
        var s2 = seed + 17;
        var s3 = seed + 31;
        net.Add(new LinearLayer(_inputDim, 64, s1));
        net.Add(new ActivationLayer(64, ActivationType.ReLu));
        net.Add(new LinearLayer(64, 64, s2));
        net.Add(new ActivationLayer(64, ActivationType.ReLu));
        net.Add(new LinearLayer(64, _actionCount, s3));
        var opt = new AdamOptimizer(lr);
        return (net, opt);
    }

    private void ScaleGrads(double scale)
    {
        foreach (var p in _net.Parameters())
        {
            for (var i = 0; i < p.Grad.Length; i++)
                p.Grad[i] *= scale;
        }
    }

    private void ClipGrads(double clip)
    {
        if (clip <= 0) return;
        double sum = 0;
        foreach (var p in _net.Parameters())
        {
            for (var i = 0; i < p.Grad.Length; i++)
                sum += p.Grad[i] * p.Grad[i];
        }
        var norm = Math.Sqrt(sum);
        if (norm <= clip || norm <= 0) return;
        var scale = clip / norm;
        foreach (var p in _net.Parameters())
        {
            for (var i = 0; i < p.Grad.Length; i++)
                p.Grad[i] *= scale;
        }
    }

    private static double[] ToDouble(float[] input)
    {
        var arr = new double[input.Length];
        for (var i = 0; i < input.Length; i++)
            arr[i] = input[i];
        return arr;
    }

    private static double[] Softmax(double[] logits)
    {
        if (logits.Length == 0) return Array.Empty<double>();
        var max = logits.Max();
        var exps = new double[logits.Length];
        double sum = 0;
        for (var i = 0; i < logits.Length; i++)
        {
            var e = Math.Exp(logits[i] - max);
            exps[i] = e;
            sum += e;
        }
        if (sum <= 0) return exps.Select(_ => 1.0 / logits.Length).ToArray();
        for (var i = 0; i < exps.Length; i++)
            exps[i] /= sum;
        return exps;
    }
}
#endif
