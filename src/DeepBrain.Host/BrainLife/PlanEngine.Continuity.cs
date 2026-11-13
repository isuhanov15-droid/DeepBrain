// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using DeepBrain.Shared.BrainDtos.V3;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class PlanEngine
{
    private sealed class ContinuityState
    {
        [JsonRequired] public string LastGoalId { get; set; } = default!;
        [JsonRequired] public PlanDto? ActivePlan { get; set; } = default!;
        [JsonRequired] public bool Created { get; set; } = default!;
        [JsonRequired] public bool Interrupted { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            LastGoalId = _lastGoalId,
            ActivePlan = ActivePlan,
            Created = Created,
            Interrupted = Interrupted
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _lastGoalId = v.LastGoalId;
        ActivePlan = v.ActivePlan;
        Created = v.Created;
        Interrupted = v.Interrupted;
    }
}
