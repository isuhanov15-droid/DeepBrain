// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using DeepBrain.Shared.BrainDtos.V3;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class CircadianClock
{
    private sealed class ContinuityState
    {
        [JsonRequired] public double TimeOfDay { get; set; } = default!;
        [JsonRequired] public double SleepPressure { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            TimeOfDay = TimeOfDay,
            SleepPressure = SleepPressure
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);
        if (v.TimeOfDay < 0 || v.TimeOfDay >= 1 || v.SleepPressure < 0 || v.SleepPressure > 1) throw new InvalidDataException("Invalid circadian state");
        TimeOfDay = v.TimeOfDay;
        SleepPressure = v.SleepPressure;
    }
}
