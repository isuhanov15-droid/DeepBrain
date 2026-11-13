// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class CurriculumManager
{
    private sealed class ContinuityState
    {
        [JsonRequired] public List<ScenarioDefinition> Scenarios { get; set; } = default!;
        [JsonRequired] public string Mode { get; set; } = default!;
        [JsonRequired] public double RewardGateThreshold { get; set; } = default!;
        [JsonRequired] public int Index { get; set; } = default!;
        [JsonRequired] public ulong RandomState { get; set; }
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            Scenarios = _scenarios,
            Mode = _mode,
            RewardGateThreshold = _rewardGateThreshold,
            Index = _index,
            RandomState = ((ContinuityRandom)_rng).State
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);
        if (v.Index < 0 || v.Index >= v.Scenarios.Count) throw new InvalidDataException("Invalid scenario position");
        _rng = new ContinuityRandom(v.RandomState);
        _scenarios.Clear();
        _scenarios.AddRange(v.Scenarios);
        _mode = v.Mode;
        _rewardGateThreshold = v.RewardGateThreshold;
        _index = v.Index;
    }

    internal void EnableContinuityRandom() => _rng = new ContinuityRandom(_continuitySeed);
}
