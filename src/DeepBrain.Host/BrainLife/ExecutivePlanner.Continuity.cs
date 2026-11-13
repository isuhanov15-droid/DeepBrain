// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed partial class ExecutivePlanner
{
    private sealed class ContinuityState
    {
        [JsonRequired] public PlannerState State { get; set; } = default!;
        [JsonRequired] public long LastSaveAttemptClock { get; set; } = default!;
        [JsonRequired] public bool ResumePending { get; set; } = default!;
        [JsonRequired] public bool Revalidated { get; set; } = default!;
        [JsonRequired] public double AppliedBias { get; set; } = default!;
        [JsonRequired] public string BiasStatus { get; set; } = default!;
        [JsonRequired] public string ScenarioResumeStatus { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        lock (_gate)
        {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            State = _state,
            LastSaveAttemptClock = _lastSaveAttemptClock,
            ResumePending = _resumePending,
            Revalidated = _revalidated,
            AppliedBias = _appliedBias,
            BiasStatus = _biasStatus,
            ScenarioResumeStatus = _scenarioResumeStatus
        }, RuntimeCheckpoint.Json);
        }
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        lock (_gate)
        {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);
        Validate(v.State);
        _state = v.State;
        _lastSaveAttemptClock = v.LastSaveAttemptClock;
        _resumePending = v.ResumePending;
        _revalidated = v.Revalidated;
        _appliedBias = v.AppliedBias;
        _biasStatus = v.BiasStatus;
        _scenarioResumeStatus = v.ScenarioResumeStatus;
        }
    }

    internal void EnsureContinuitySaved() { if (_dirty || _lastError is not null) throw new IOException("Unsaved ExecutivePlanner: " + _lastError); }
}
