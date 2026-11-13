// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class MessageDedupeGuard
{
    private sealed class ContinuityState
    {
        [JsonRequired] public long LastTick { get; set; } = default!;
        [JsonRequired] public string? LastMessage { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            LastTick = _lastTick,
            LastMessage = _lastMessage
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _lastTick = v.LastTick;
        _lastMessage = v.LastMessage;
    }
}
