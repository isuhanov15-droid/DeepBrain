// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using DeepBrain.Shared.Brain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class EpisodeMemory
{
    private sealed class ContinuityState
    {
        [JsonRequired] public List<EpisodeDto> Episodes { get; set; } = default!;
        [JsonRequired] public List<MemoryRecord> Records { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            Episodes = _episodes,
            Records = _records
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);
        if (v.Episodes.Count > 1000 || v.Records.Count > 1000) throw new InvalidDataException("Invalid recent memory");
        _episodes.Clear();
        _episodes.AddRange(v.Episodes);
        _records.Clear();
        _records.AddRange(v.Records);
    }
}
