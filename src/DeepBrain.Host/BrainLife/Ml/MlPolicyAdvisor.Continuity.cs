// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.IO;
using System.Linq;
using System.Text.Json;
using DeepBrain.Shared.BrainDtos.V6;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed partial class MlPolicyAdvisor
{
    private sealed class ContinuityState
    {
        [JsonRequired] public Queue<double> LossWindow { get; set; } = default!;
        [JsonRequired] public Queue<bool> OverrideWindow { get; set; } = default!;
        [JsonRequired] public double Entropy { get; set; } = default!;
        [JsonRequired] public string PolicySource { get; set; } = default!;
        [JsonRequired] public double LastNetWeight { get; set; } = default!;
        [JsonRequired] public double LastEpsilon { get; set; } = default!;
        [JsonRequired] public double AvgQ { get; set; } = default!;
        [JsonRequired] public int NanSkips { get; set; } = default!;
        [JsonRequired] public int IllegalChoiceCount { get; set; } = default!;
        [JsonRequired] public int OverrideCount { get; set; } = default!;
        [JsonRequired] public int InvalidActionFallbackCount { get; set; } = default!;
        [JsonRequired] public double Epsilon { get; set; } = default!;
        [JsonRequired] public string BackendKind { get; set; } = default!;
        [JsonRequired] public long RestoredTrainSteps { get; set; } = default!;
        [JsonRequired] public ulong RandomState { get; set; }
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            LossWindow = _lossWindow,
            OverrideWindow = _overrideWindow,
            Entropy = _entropy,
            PolicySource = _policySource,
            LastNetWeight = _lastNetWeight,
            LastEpsilon = _lastEpsilon,
            AvgQ = _avgQ,
            NanSkips = _nanSkips,
            IllegalChoiceCount = _illegalChoiceCount,
            OverrideCount = _overrideCount,
            InvalidActionFallbackCount = _invalidActionFallbackCount,
            Epsilon = _epsilon,
            BackendKind = _backendKind,
            RestoredTrainSteps = _restoredTrainSteps,
            RandomState = ((ContinuityRandom)_rng).State
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _rng = new ContinuityRandom(v.RandomState);
        _lossWindow.Clear();
        foreach (var item in v.LossWindow) _lossWindow.Enqueue(item);
        _overrideWindow.Clear();
        foreach (var item in v.OverrideWindow) _overrideWindow.Enqueue(item);
        _entropy = v.Entropy;
        _policySource = v.PolicySource;
        _lastNetWeight = v.LastNetWeight;
        _lastEpsilon = v.LastEpsilon;
        _avgQ = v.AvgQ;
        _nanSkips = v.NanSkips;
        _illegalChoiceCount = v.IllegalChoiceCount;
        _overrideCount = v.OverrideCount;
        _invalidActionFallbackCount = v.InvalidActionFallbackCount;
        _epsilon = v.Epsilon;
        _backendKind = v.BackendKind;
        _restoredTrainSteps = v.RestoredTrainSteps;
    }

    internal void EnableContinuityRandom() => _rng = new ContinuityRandom(_continuitySeed);
}
