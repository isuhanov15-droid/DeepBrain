// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class ActionCooldowns
{
    private sealed class ContinuityState
    {
        [JsonRequired] public Dictionary<string, long> LastTick { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            LastTick = _lastTick
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _lastTick.Clear();
        foreach (var pair in v.LastTick) _lastTick.Add(pair.Key, pair.Value);
    }
}
