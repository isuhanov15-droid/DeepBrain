// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V5;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class HabitSystem
{
    private sealed class ContinuityState
    {
        [JsonRequired] public Dictionary<string, HabitPersistentState> Habits { get; set; } = default!;
        [JsonRequired] public Dictionary<string, Queue<long>> RecentUses { get; set; } = default!;
        [JsonRequired] public long ClockTick { get; set; } = default!;
        [JsonRequired] public long ClockOffset { get; set; } = default!;
        [JsonRequired] public long LastSessionTick { get; set; } = default!;
        [JsonRequired] public bool ClockChanged { get; set; } = default!;
        [JsonRequired] public long LastDecayBucket { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        lock (_gate)
        {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            Habits = _habits,
            RecentUses = _recentUses,
            ClockTick = _clockTick,
            ClockOffset = _clockOffset,
            LastSessionTick = _lastSessionTick,
            ClockChanged = _clockChanged,
            LastDecayBucket = _lastDecayBucket
        }, RuntimeCheckpoint.Json);
        }
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        lock (_gate)
        {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _habits.Clear();
        foreach (var pair in v.Habits) _habits.Add(pair.Key, pair.Value);
        _recentUses.Clear();
        foreach (var pair in v.RecentUses) _recentUses.Add(pair.Key, pair.Value);
        _clockTick = v.ClockTick;
        _clockOffset = v.ClockOffset;
        _lastSessionTick = v.LastSessionTick;
        _clockChanged = v.ClockChanged;
        _lastDecayBucket = v.LastDecayBucket;
        }
    }

    internal void EnsureContinuitySaved() { if (_config.Enable && (_dirtyUpdates > 0 || _lastError is not null)) throw new IOException("Unsaved habits: " + _lastError); }
}
