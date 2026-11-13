// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using DeepBrain.Shared.BrainDtos.V4;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class AttentionInertiaEngine
{
    private sealed class ContinuityState
    {
        [JsonRequired] public string LastFocus { get; set; } = default!;
        [JsonRequired] public double LastIntensity { get; set; } = default!;
        [JsonRequired] public int PendingTicks { get; set; } = default!;
        [JsonRequired] public string? PendingFocus { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            LastFocus = _lastFocus,
            LastIntensity = _lastIntensity,
            PendingTicks = _pendingTicks,
            PendingFocus = _pendingFocus
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _lastFocus = v.LastFocus;
        _lastIntensity = v.LastIntensity;
        _pendingTicks = v.PendingTicks;
        _pendingFocus = v.PendingFocus;
    }
}
