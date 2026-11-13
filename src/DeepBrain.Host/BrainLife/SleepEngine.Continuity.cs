// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class SleepEngine
{
    private sealed class ContinuityState
    {
        [JsonRequired] public bool IsSleeping { get; set; } = default!;
        [JsonRequired] public bool EnteredSleep { get; set; } = default!;
        [JsonRequired] public bool WokeUp { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            IsSleeping = IsSleeping,
            EnteredSleep = EnteredSleep,
            WokeUp = WokeUp
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        IsSleeping = v.IsSleeping;
        EnteredSleep = v.EnteredSleep;
        WokeUp = v.WokeUp;
    }
}
