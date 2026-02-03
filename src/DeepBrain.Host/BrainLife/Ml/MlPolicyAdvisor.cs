using System.Linq;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed class MlPolicyAdvisor
{
    private readonly StateVectorizer _vectorizer = new();
    private ExperienceBuffer _buffer;
    private PolicyNetAdapter _net;
    private readonly Random _rng;
    private readonly OnlineTrainer _trainer;

    private double _entropy;
    private string _policySource = "heuristic";

    public MlPolicyAdvisor(MlConfig config)
    {
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
    }

    public PolicyDecision SelectAction(float[] stateVec, IReadOnlyDictionary<string, double> heuristicScores, IReadOnlyList<string> allowedActions, MlConfig config, long tick)
    {
        if (!config.Enable)
            return SelectHeuristic(heuristicScores, allowedActions);

        if (stateVec.Length != _net.InputDim)
            return SelectHeuristic(heuristicScores, allowedActions);

        var probs = _net.PredictProbs(stateVec);
        if (probs.Length != ActionCatalog.Count || probs.Any(p => float.IsNaN(p) || float.IsInfinity(p)))
            return SelectHeuristic(heuristicScores, allowedActions);

        _entropy = ComputeEntropy(probs);
        var netWeight = ComputeNetWeight(config, _buffer.Count);
        var epsilon = ComputeEpsilon(config, _trainer.TrainSteps);

        var finalScores = BlendScores(heuristicScores, probs, netWeight);
        var actionName = ChooseAction(finalScores, allowedActions, epsilon, _rng);

        _policySource = netWeight <= 0.01 ? "heuristic" : netWeight >= 0.6 ? "net" : "blend";
        return new PolicyDecision(actionName, netWeight, epsilon, _entropy, _policySource);
    }

    public void Observe(float[] state, int actionIdx, float reward, float[] nextState, MlConfig config, long tick)
    {
        if (!config.Enable) return;
        if (actionIdx < 0 || actionIdx >= ActionCatalog.Count) return;
        if (state.Length != _net.InputDim || nextState.Length != _net.InputDim) return;
        _buffer.Add(new Transition(state, actionIdx, reward, nextState, Done: false));
        _trainer.TryTrain(tick, config);
    }

    public MlPolicyDto BuildTelemetry(bool enabled, int inputDim, int actionCount, double avgReward200)
    {
        if (!enabled)
        {
            return new MlPolicyDto(
                Enabled: false,
                InputDim: inputDim,
                ActionCount: actionCount,
                NetWeight: 0,
                Epsilon: 0,
                BufferSize: _buffer.Count,
                LastLoss: _trainer.LastLoss,
                TrainSteps: _trainer.TrainSteps,
                AvgReward200: avgReward200,
                Entropy: 0,
                PolicySource: "heuristic"
            );
        }
        return new MlPolicyDto(
            Enabled: enabled,
            InputDim: inputDim,
            ActionCount: actionCount,
            NetWeight: ComputeNetWeightLast(),
            Epsilon: ComputeEpsilonLast(),
            BufferSize: _buffer.Count,
            LastLoss: _trainer.LastLoss,
            TrainSteps: _trainer.TrainSteps,
            AvgReward200: avgReward200,
            Entropy: _entropy,
            PolicySource: _policySource
        );
    }

    public void TrySave(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _net.Save(path);
    }

    public bool TryLoad(string path) => _net.TryLoad(path);

    private double _lastNetWeight;
    private double _lastEpsilon;

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

    private double ComputeNetWeightLast() => _lastNetWeight;
    private double ComputeEpsilonLast() => _lastEpsilon;

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
    private PolicyDecision SelectHeuristic(IReadOnlyDictionary<string, double> scores, IReadOnlyList<string> allowed)
    {
        var dict = scores.ToDictionary(kv => kv.Key, kv => kv.Value);
        var action = ChooseAction(dict, allowed, 0, _rng);
        _policySource = "heuristic";
        return new PolicyDecision(action, 0, 0, _entropy, "heuristic");
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

public readonly record struct PolicyDecision(
    string ActionName,
    double NetWeight,
    double Epsilon,
    double Entropy,
    string PolicySource)
{ }
