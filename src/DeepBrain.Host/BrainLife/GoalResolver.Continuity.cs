// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class GoalResolver
{
    private sealed class ContinuityState
    {
        [JsonRequired] public Dictionary<string, double> Satisfaction { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            Satisfaction = _satisfaction
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _satisfaction.Clear();
        foreach (var pair in v.Satisfaction) _satisfaction.Add(pair.Key, pair.Value);
    }
}
