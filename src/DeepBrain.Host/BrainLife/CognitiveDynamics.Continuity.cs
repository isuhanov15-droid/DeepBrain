// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed partial class CognitiveDynamics
{
    private sealed class ContinuityState
    {
        [JsonRequired] public State State { get; set; } = default!;
        [JsonRequired] public long LastAttempt { get; set; } = default!;
        [JsonRequired] public bool Sleeping { get; set; } = default!;
        [JsonRequired] public bool SeedOutcomes { get; set; } = default!;
        [JsonRequired] public HashSet<string> SeenOutcomes { get; set; } = default!;
        [JsonRequired] public double? PriorThreat { get; set; } = default!;
        [JsonRequired] public CognitiveObservation? Current { get; set; } = default!;
        [JsonRequired] public string? Goal { get; set; } = default!;
        [JsonRequired] public ActionExpectationDto? Expectation { get; set; } = default!;
        [JsonRequired] public EmotionSignalDto[] Signals { get; set; } = default!;
        [JsonRequired] public Dictionary<string, double> Biases { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        lock (_gate)
        {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            State = _state,
            LastAttempt = _lastAttempt,
            Sleeping = _sleeping,
            SeedOutcomes = _seedOutcomes,
            SeenOutcomes = _seenOutcomes,
            PriorThreat = _priorThreat,
            Current = _current,
            Goal = _goal,
            Expectation = _expectation,
            Signals = _signals,
            Biases = _biases
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
        _lastAttempt = v.LastAttempt;
        _sleeping = v.Sleeping;
        _seedOutcomes = v.SeedOutcomes;
        _seenOutcomes.Clear();
        _seenOutcomes.UnionWith(v.SeenOutcomes);
        _priorThreat = v.PriorThreat;
        _current = v.Current;
        _goal = v.Goal;
        _expectation = v.Expectation;
        _signals = v.Signals;
        _biases = v.Biases;
        }
    }

    internal void EnsureContinuitySaved() { if (_dirty || _lastError is not null) throw new IOException("Unsaved CognitiveDynamics: " + _lastError); }
}
