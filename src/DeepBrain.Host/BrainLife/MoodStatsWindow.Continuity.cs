// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using DeepBrain.Shared.BrainDtos.V6;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class MoodStatsWindow
{
    private sealed class ContinuityState
    {
        [JsonRequired] public Dictionary<string, int> Counts { get; set; } = default!;
        [JsonRequired] public int Total { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            Counts = _counts,
            Total = _total
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _counts.Clear();
        foreach (var pair in v.Counts) _counts.Add(pair.Key, pair.Value);
        _total = v.Total;
    }
}
