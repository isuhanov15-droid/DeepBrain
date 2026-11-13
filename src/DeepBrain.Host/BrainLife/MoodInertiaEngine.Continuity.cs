// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using DeepBrain.Shared.Brain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class MoodInertiaEngine
{
    private sealed class ContinuityState
    {
        [JsonRequired] public string CurrentMood { get; set; } = default!;
        [JsonRequired] public string CandidateMood { get; set; } = default!;
        [JsonRequired] public int CandidateTicks { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            CurrentMood = _currentMood,
            CandidateMood = _candidateMood,
            CandidateTicks = _candidateTicks
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _currentMood = v.CurrentMood;
        _candidateMood = v.CandidateMood;
        _candidateTicks = v.CandidateTicks;
    }
}
