// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class SelfTalkThrottle
{
    private sealed class ContinuityState
    {
        [JsonRequired] public long CooldownTicks { get; set; } = default!;
        [JsonRequired] public long RepeatCooldownTicks { get; set; } = default!;
        [JsonRequired] public long SemanticCooldownTicks { get; set; } = default!;
        [JsonRequired] public string? LastText { get; set; } = default!;
        [JsonRequired] public long LastTick { get; set; } = default!;
        [JsonRequired] public Dictionary<string, long> SemanticLastTicks { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            CooldownTicks = _cooldownTicks,
            RepeatCooldownTicks = _repeatCooldownTicks,
            SemanticCooldownTicks = _semanticCooldownTicks,
            LastText = _lastText,
            LastTick = _lastTick,
            SemanticLastTicks = _semanticLastTicks
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _cooldownTicks = v.CooldownTicks;
        _repeatCooldownTicks = v.RepeatCooldownTicks;
        _semanticCooldownTicks = v.SemanticCooldownTicks;
        _lastText = v.LastText;
        _lastTick = v.LastTick;
        _semanticLastTicks.Clear();
        foreach (var pair in v.SemanticLastTicks) _semanticLastTicks.Add(pair.Key, pair.Value);
    }
}
