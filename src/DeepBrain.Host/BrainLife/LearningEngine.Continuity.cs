// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class LearningEngine
{
    private sealed class ContinuityState
    {
        [JsonRequired] public Dictionary<string, double> EmaReward { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            EmaReward = _emaReward
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _emaReward.Clear();
        foreach (var pair in v.EmaReward) _emaReward.Add(pair.Key, pair.Value);
    }
}
