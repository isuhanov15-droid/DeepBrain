using System.IO;
using System.Linq;
using System.Text.Json;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed class MlPolicyAdvisor : IMlPolicyAdvisor
{
    private readonly StateVectorizer _vectorizer = new();
    private readonly Action<string> _log;
    private IBrainMlBackend _backend;
    private readonly Random _rng;

    private readonly Queue<double> _lossWindow = new(100);
    private readonly Queue<bool> _overrideWindow = new(200);

    private double _entropy;
    private string _policySource = "heuristic";
    private double _lastNetWeight;
    private double _lastEpsilon;
    private double _avgQ;
    private int _nanSkips;
    private int _illegalChoiceCount;
    private int _overrideCount;
    private int _invalidActionFallbackCount;
    private double _epsilon = -1;
    private string _backendKind = "stub";
    private long _restoredTrainSteps;
    private DateTime _lastBackendLog = DateTime.MinValue;
    private string? _lastBackendLogMsg;

    public MlPolicyAdvisor(MlConfig config, Action<string> log)
        : this(config, log, MlBackendFactory.Create(config, log))
    {
    }

    public MlPolicyAdvisor(MlConfig config, Action<string> log, IBrainMlBackend backend)
    {
        _log = log;
        _rng = new Random(config.Seed);
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _backendKind = _backend.Kind;
    }

    public float[] Encode(StateVectorInput input) => _vectorizer.Encode(input);

    public void Reset(MlConfig config)
    {
        _backend.UpdateConfig(config);
        _backend.Reset(config);
        _entropy = 0;
        _avgQ = 0;
        _policySource = "heuristic";
        _lastNetWeight = 0;
        _lastEpsilon = 0;
        _nanSkips = 0;
        _illegalChoiceCount = 0;
        _overrideCount = 0;
        _invalidActionFallbackCount = 0;
        _restoredTrainSteps = 0;
        _epsilon = config.EpsilonStart;
        _lossWindow.Clear();
        _overrideWindow.Clear();
    }

    public void ResetCounters()
    {
        _nanSkips = 0;
        _illegalChoiceCount = 0;
        _overrideCount = 0;
        _invalidActionFallbackCount = 0;
        _lossWindow.Clear();
        _overrideWindow.Clear();
        _backend.ResetCounters();
    }

    public PolicyDecision SelectAction(float[] stateVec, IReadOnlyDictionary<string, double> heuristicScores, IReadOnlyList<string> allowedActions, float[] actionMask, MlConfig config, long tick)
    {
        EnsureBackend(config);
        var heuristicBest = PickHeuristicBest(heuristicScores, allowedActions);
        if (!config.Enable || !_backend.IsAvailable || stateVec.Length != StateVectorizer.InputDim)
            return new PolicyDecision(heuristicBest, 0, 0, 0, "heuristic", false, false, false);

        MlInferResult infer;
        try
        {
            infer = _backend.InferAsync(stateVec, actionMask, config, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log($"Предупреждение: ошибка ml.infer: {ex.Message}");
            return new PolicyDecision(heuristicBest, 0, 0, 0, "heuristic", false, false, false);
        }

        if (!infer.Ok || infer.QValues.Length != ActionCatalog.Count || infer.QValues.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            return new PolicyDecision(heuristicBest, 0, 0, 0, "heuristic", false, false, false);

        var probs = infer.Probabilities is { Length: > 0 }
            ? infer.Probabilities.Select(v => (double)v).ToArray()
            : MlMath.Softmax(infer.QValues);
        if (probs.Length != ActionCatalog.Count)
            probs = MlMath.Softmax(infer.QValues);
        _avgQ = infer.AvgQ;

        var mask = config.ActionMasking ? MlMath.NormalizeMask(actionMask) : null;
        if (mask is not null)
            MlMath.ApplyMask(probs, mask);

        _entropy = MlMath.ComputeEntropy(probs);
        var effectiveTrainSteps = EffectiveTrainSteps();
        var netWeight = ComputeNetWeight(config, _backend.BufferSize, effectiveTrainSteps);
        var epsilon = ComputeEpsilon(config, _backend.BufferSize, effectiveTrainSteps);

        var finalScores = BlendScores(heuristicScores, probs.Select(p => (float)p).ToArray(), netWeight);
        var actionName = ChooseAction(finalScores, allowedActions, epsilon, _rng);
        var illegal = !allowedActions.Contains(actionName);
        var maskedOut = mask is not null && MlMath.IsMaskedOut(actionName, mask);
        if (illegal)
        {
            _illegalChoiceCount++;
            _invalidActionFallbackCount++;
            actionName = heuristicBest;
        }
        else if (maskedOut)
        {
            _invalidActionFallbackCount++;
            actionName = heuristicBest;
        }

        var usedMl = netWeight > 0.01 && !illegal && !maskedOut;
        var overrideHeuristic = usedMl && actionName != heuristicBest;
        PushOverride(overrideHeuristic);

        _policySource = netWeight <= 0.01 ? "heuristic" : netWeight >= 0.6 ? "net" : "blend";
        return new PolicyDecision(actionName, netWeight, epsilon, _entropy, _policySource, usedMl, illegal || maskedOut, overrideHeuristic);
    }

    public void Observe(float[] state, int actionIdx, float reward, float[] nextState, bool done, float[] nextActionMask, MlConfig config, long tick)
    {
        if (!config.Enable || !_backend.IsAvailable) return;
        if (actionIdx < 0 || actionIdx >= ActionCatalog.Count) return;
        if (state.Length != StateVectorizer.InputDim || nextState.Length != StateVectorizer.InputDim) return;

        var clampedReward = (float)Math.Clamp(reward, -1.0, 1.0);
        var mask = config.ActionMasking ? MlMath.NormalizeMask(nextActionMask) : null;
        mask ??= new float[ActionCatalog.Count];
        if (mask.All(v => v == 0f))
        {
            for (var i = 0; i < mask.Length; i++) mask[i] = 1f;
        }

        var transition = new Transition(state, actionIdx, clampedReward, nextState, done, mask);
        var result = _backend.TrainAsync(transition, config, tick, CancellationToken.None).GetAwaiter().GetResult();
        if (result.IsNaN)
        {
            _nanSkips++;
            if (tick % 200 == 0)
                _log("Предупреждение: шаг обучения ML пропущен из-за NaN/Inf");
            return;
        }

        if (result.Trained)
        {
            PushLoss(result.Loss);
            _restoredTrainSteps = Math.Max(_restoredTrainSteps, result.TrainSteps);
        }
    }

    public bool TryLoad(string path, out int episodeId)
    {
        episodeId = 0;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            var meta = JsonSerializer.Deserialize<MlCheckpointMeta>(json);
            if (meta is not null && !string.IsNullOrWhiteSpace(meta.WeightsPath))
            {
                if (_backend.TryLoad(meta.WeightsPath, out _))
                {
                    episodeId = meta.EpisodeId;
                    _epsilon = meta.Epsilon;
                    _restoredTrainSteps = Math.Max(0, meta.TrainSteps);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _log($"Предупреждение: метаданные ML checkpoint повреждены: {ex.Message}");
        }

        var loaded = _backend.TryLoad(path, out episodeId);
        if (loaded)
            _restoredTrainSteps = Math.Max(_restoredTrainSteps, _backend.TrainSteps);
        return loaded;
    }

    public void TrySave(string path, int episodeId)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        var backendPath = _backend.Kind == "remote" ? path : path + ".net";
        _backend.TrySave(backendPath, episodeId);
        var meta = new MlCheckpointMeta(episodeId, _epsilon, EffectiveTrainSteps(), backendPath);
        var json = JsonSerializer.Serialize(meta);
        File.WriteAllText(path, json);
    }

    public MlPolicyDto BuildTelemetry(bool enabled, bool coreAvailable, int inputDim, int actionCount, double avgReward200, string? reasonIfDisabled, string mlMode, bool trainEnabled, int trainingEpisodeCount, int evalEpisodeCount)
    {
        if (!enabled)
        {
            return new MlPolicyDto(
                Enabled: false,
                CoreAvailable: coreAvailable,
                InputDim: inputDim,
                ActionCount: actionCount,
                BufferSize: _backend.BufferSize,
                BufferCapacity: _backend.BufferCapacity,
                Epsilon: 0,
                NetWeight: 0,
                LastLoss: _backend.LastLoss,
                AvgLoss100: AverageLoss(),
                AvgReward200: avgReward200,
                AvgQ: _avgQ,
                Entropy: 0,
                TrainSteps: EffectiveTrainSteps(),
                NanSkips: _nanSkips,
                IllegalChoiceCount: _illegalChoiceCount,
                OverrideCount: _overrideCount,
                InvalidActionFallbackCount: _invalidActionFallbackCount,
                PolicySource: "heuristic",
                BackendKind: _backendKind,
                RemoteConnected: _backend.IsConnected,
                RttMs: _backend.LastRttMs,
                LastRemoteError: _backend.LastError,
                ReasonIfDisabled: reasonIfDisabled,
                MlMode: mlMode,
                TrainEnabled: trainEnabled,
                TrainingEpisodeCount: trainingEpisodeCount,
                EvalEpisodeCount: evalEpisodeCount
            );
        }

        return new MlPolicyDto(
            Enabled: enabled,
            CoreAvailable: coreAvailable,
            InputDim: inputDim,
            ActionCount: actionCount,
            BufferSize: _backend.BufferSize,
            BufferCapacity: _backend.BufferCapacity,
            Epsilon: _lastEpsilon,
            NetWeight: _lastNetWeight,
            LastLoss: _backend.LastLoss,
            AvgLoss100: AverageLoss(),
            AvgReward200: avgReward200,
            AvgQ: _avgQ,
            Entropy: _entropy,
            TrainSteps: EffectiveTrainSteps(),
            NanSkips: _nanSkips,
            IllegalChoiceCount: _illegalChoiceCount,
            OverrideCount: _overrideCount,
            InvalidActionFallbackCount: _invalidActionFallbackCount,
            PolicySource: _policySource,
            BackendKind: _backendKind,
            RemoteConnected: _backend.IsConnected,
            RttMs: _backend.LastRttMs,
            LastRemoteError: _backend.LastError,
            ReasonIfDisabled: reasonIfDisabled,
            MlMode: mlMode,
            TrainEnabled: trainEnabled,
            TrainingEpisodeCount: trainingEpisodeCount,
            EvalEpisodeCount: evalEpisodeCount
        );
    }

    public bool TryConnectRemote() => _backend.TryConnect();
    public void DisconnectRemote() => _backend.Disconnect();

    private void EnsureBackend(MlConfig config)
    {
        var desired = (config.Backend ?? "off").Trim().ToLowerInvariant();
        if (!config.Enable)
            desired = "off";

        if (desired == _backendKind)
        {
            _backend.UpdateConfig(config);
            return;
        }

        var old = _backend;
        _backend = MlBackendFactory.Create(config, _log);
        _backendKind = _backend.Kind;
        _restoredTrainSteps = 0;
        _ = old.DisposeAsync();
        if (config.LogBackendSwitches)
            RateLimitedBackendLog($"ML backend переключён на {_backendKind}");
    }

    private void RateLimitedBackendLog(string message)
    {
        var now = DateTime.UtcNow;
        if (string.Equals(_lastBackendLogMsg, message, StringComparison.Ordinal)
            && (now - _lastBackendLog).TotalSeconds < 10)
            return;
        _lastBackendLog = now;
        _lastBackendLogMsg = message;
        _log(message);
    }

    private double ComputeNetWeight(MlConfig config, int bufferSize, long trainSteps)
    {
        var w = MlPolicySchedule.ComputeNetWeight(config, bufferSize, trainSteps);
        _lastNetWeight = w;
        return w;
    }

    private long EffectiveTrainSteps()
    {
        return Math.Max(_restoredTrainSteps, _backend.TrainSteps);
    }

    private double ComputeEpsilon(MlConfig config, int bufferSize, long trainSteps)
    {
        _epsilon = MlPolicySchedule.NextEpsilon(config, _epsilon, bufferSize, trainSteps);
        var eps = _epsilon;
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

    private sealed record MlCheckpointMeta(int EpisodeId, double Epsilon, long TrainSteps, string WeightsPath);
}
