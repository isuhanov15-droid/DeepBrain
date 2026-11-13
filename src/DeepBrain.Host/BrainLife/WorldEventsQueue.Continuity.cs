// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Linq;
using DeepBrain.Shared.BrainDtos.V4;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class WorldEventsQueue
{
    private sealed class ContinuityState
    {
        [JsonRequired] public List<WorldEventDto> Events { get; set; } = default!;
        [JsonRequired] public int ConsumedCount { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            Events = _events,
            ConsumedCount = _consumedCount
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);
        if (v.Events.Count > 50 || v.ConsumedCount < 0) throw new InvalidDataException("Invalid event queue");
        _events.Clear();
        _events.AddRange(v.Events);
        _consumedCount = v.ConsumedCount;
    }
}
