#if ML_CORE
using System.Linq;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed class MlPolicyAdvisorReal : IMlPolicyAdvisor
{
    private readonly StateVectorizer _vectorizer = new();
    private ExperienceBuffer _buffer;
    private PolicyNetAdapter _net;
    private readonly Random _rng;
    private readonly OnlineTrainer _trainer;
    private readonly Action<string> _log;

    private readonly Queue<double> _lossWindow = new(100);
    private readonly Queue<bool> _overrideWindow = new(200);

    private double _entropy;
    private string _policySource = "heuristic";
    private double _lastNetWeight;
    private double _lastEpsilon;
    private int _nanSkips;
    private int _illegalChoiceCount;
    private int _overrideCount;

    public MlPolicyAdvisorReal(MlConfig config, Action<string> log)
    {
        _log = log;
        _rng = new Random(config.Seed);
        _buffer = new ExperienceBuffer(Math.Max(1024, config.BufferSize));
        _net = new PolicyNetAdapter(StateVectorizer.InputDim, ActionCatalog.Count, config.Seed, config.LearningRate);
        _trainer = new OnlineTrainer(_buffer, _net, _rng);
    }

    public float[] Encode(StateVectorInput input) => _vectorizer.Encode(input);

    public void Reset(MlConfig config)
    {
        _buffer = new ExperienceBuffer(Math.Max(1024, config.BufferSize));
        _net.Reset(config.Seed, config.LearningRate);
        _entropy = 0;
        _policySource = "heuristic";
        _lastNetWeight = 0;
        _lastEpsilon = 0;
        _nanSkips = 0;
        _illegalChoiceCount = 0;
        _overrideCount = 0;
        _lossWindow.Clear();
        _overrideWindow.Clear();
    }

    public void ResetCounters()
    {
        _nanSkips = 0;
        _illegalChoiceCount = 0;
        _overrideCount = 0;
        _lossWindow.Clear();
        _overrideWindow.Clear();
    }

    public PolicyDecision SelectAction(float[] stateVec, IReadOnlyDictionary<string, double> heuristicScores, IReadOnlyList<string> allowedActions, MlConfig config, long tick)
    {
        var heuristicBest = PickHeuristicBest(heuristicScores, allowedActions);
        if (!config.Enable || stateVec.Length != _net.InputDim)
            return new PolicyDecision(heuristicBest, 0, 0, 0, "heuristic", false, false, false);

        var probs = _net.PredictProbs(stateVec);
        if (probs.Length != ActionCatalog.Count || probs.Any(p => float.IsNaN(p) || float.IsInfinity(p)))
            return new PolicyDecision(heuristicBest, 0, 0, 0, "heuristic", false, false, false);

        _entropy = ComputeEntropy(probs);
        var netWeight = ComputeNetWeight(config, _buffer.Count);
        var epsilon = ComputeEpsilon(config, _trainer.TrainSteps);

        var finalScores = BlendScores(heuristicScores, probs, netWeight);
        var actionName = ChooseAction(finalScores, allowedActions, epsilon, _rng);
        var illegal = !allowedActions.Contains(actionName);
        if (illegal)
        {
            _illegalChoiceCount++;
            actionName = heuristicBest;
        }

        var usedMl = netWeight > 0.01 && !illegal;
        var overrideHeuristic = usedMl && actionName != heuristicBest;
        PushOverride(overrideHeuristic);

        _policySource = netWeight <= 0.01 ? "heuristic" : netWeight >= 0.6 ? "net" : "blend";
        return new PolicyDecision(actionName, netWeight, epsilon, _entropy, _policySource, usedMl, illegal, overrideHeuristic);
    }

    public void Observe(float[] state, int actionIdx, float reward, float[] nextState, MlConfig config, long tick)
    {
        if (!config.Enable) return;
        if (actionIdx < 0 || actionIdx >= ActionCatalog.Count) return;
        if (state.Length != _net.InputDim || nextState.Length != _net.InputDim) return;

        var clampedReward = (float)Math.Clamp(reward, -1.0, 1.0);
        _buffer.Add(new Transition(state, actionIdx, clampedReward, nextState, Done: false));
        var result = _trainer.TryTrain(tick, config);
        if (result.IsNaN)
        {
            _nanSkips++;
            if (tick % 200 == 0)
                _log("warn: ML train skipped due to NaN/Inf");
            return;
        }

        if (result.Trained)
            PushLoss(result.Loss);
    }

    public MlPolicyDto BuildTelemetry(bool enabled, int inputDim, int actionCount, double avgReward200)
    {
        if (!enabled)
        {
            return new MlPolicyDto(
                Enabled: false,
                InputDim: inputDim,
                ActionCount: actionCount,
                BufferSize: _buffer.Count,
                BufferCapacity: _buffer.Capacity,
                Epsilon: 0,
                NetWeight: 0,
                LastLoss: _trainer.LastLoss,
                AvgLoss100: AverageLoss(),
                AvgReward200: avgReward200,
                Entropy: 0,
                TrainSteps: _trainer.TrainSteps,
                NanSkips: _nanSkips,
                IllegalChoiceCount: _illegalChoiceCount,
                OverrideCount: _overrideCount,
                PolicySource: "heuristic"
            );
        }

        return new MlPolicyDto(
            Enabled: enabled,
            InputDim: inputDim,
            ActionCount: actionCount,
            BufferSize: _buffer.Count,
            BufferCapacity: _buffer.Capacity,
            Epsilon: _lastEpsilon,
            NetWeight: _lastNetWeight,
            LastLoss: _trainer.LastLoss,
            AvgLoss100: AverageLoss(),
            AvgReward200: avgReward200,
            Entropy: _entropy,
            TrainSteps: _trainer.TrainSteps,
            NanSkips: _nanSkips,
            IllegalChoiceCount: _illegalChoiceCount,
            OverrideCount: _overrideCount,
            PolicySource: _policySource
        );
    }

    public void TrySave(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _net.Save(path);
    }

    public bool TryLoad(string path) => _net.TryLoad(path);

    private double ComputeNetWeight(MlConfig config, int bufferSize)
    {
        var warm = Math.Max(1.0, config.NetWeightWarmup);
        var t = Math.Clamp(bufferSize / warm, 0.0, 1.0);
        var w = config.NetWeightMax * t;
        _lastNetWeight = w;
        return w;
    }

    private double ComputeEpsilon(MlConfig config, long steps)
    {
        var eps = config.EpsilonStart * Math.Pow(config.EpsilonDecay, steps);
        eps = Math.Max(config.EpsilonEnd, eps);
        _lastEpsilon = eps;
        return eps;
    }

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

    private void PushOverride(bool overrideHeuristic)
    {
        if (_overrideWindow.Count >= 200) _overrideWindow.Dequeue();
        _overrideWindow.Enqueue(overrideHeuristic);
        _overrideCount = _overrideWindow.Count(v => v);
    }

    private static string PickHeuristicBest(IReadOnlyDictionary<string, double> scores, IReadOnlyList<string> allowed)
    {
        return scores
            .Where(kv => allowed.Count == 0 || allowed.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault() ?? (allowed.Count > 0 ? allowed[0] : ActionCatalog.Actions[0]);
    }

    private static string ChooseAction(Dictionary<string, double> scores, IReadOnlyList<string> allowed, double epsilon, Random rng)
    {
        var filtered = scores
            .Where(kv => allowed.Count == 0 || allowed.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .ToList();

        if (filtered.Count == 0)
            return allowed.Count > 0 ? allowed[0] : ActionCatalog.Actions[0];

        if (epsilon > 0 && rng.NextDouble() < epsilon)
        {
            var topK = Math.Min(3, filtered.Count);
            var pick = rng.Next(topK);
            return filtered[pick].Key;
        }

        return filtered[0].Key;
    }

    private static Dictionary<string, double> BlendScores(IReadOnlyDictionary<string, double> heuristicScores, float[] probs, double netWeight)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var finite = heuristicScores.Values.Where(v => !double.IsNegativeInfinity(v)).ToList();
        var max = finite.Count > 0 ? finite.Max() : 1.0;
        var min = finite.Count > 0 ? finite.Min() : 0.0;
        var range = Math.Max(1e-6, max - min);

        for (var i = 0; i < ActionCatalog.Actions.Length; i++)
        {
            var name = ActionCatalog.Actions[i];
            heuristicScores.TryGetValue(name, out var h);
            if (double.IsNegativeInfinity(h))
                h = min;
            var norm = (h - min) / range;
            var blended = (1 - netWeight) * norm + netWeight * probs[i];
            scores[name] = blended;
        }

        return scores;
    }

    private static double ComputeEntropy(float[] probs)
    {
        double sum = 0;
        for (var i = 0; i < probs.Length; i++)
        {
            var p = Math.Clamp(probs[i], 1e-6f, 1.0f);
            sum -= p * Math.Log(p);
        }
        return sum / Math.Log(probs.Length);
    }
}
#endif
