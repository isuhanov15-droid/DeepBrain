// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Linq;
using DeepBrain.Shared.BrainDtos.V4;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class SemanticMemory
{
    private sealed class ContinuityState
    {
        [JsonRequired] public Dictionary<string, Dictionary<string, Stat>> Map { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            Map = _map
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _map.Clear();
        foreach (var pair in v.Map) _map.Add(pair.Key, pair.Value);
    }
}
