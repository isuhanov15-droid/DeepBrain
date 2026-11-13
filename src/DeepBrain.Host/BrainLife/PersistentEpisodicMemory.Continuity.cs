// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class PersistentEpisodicMemory
{
    internal void ConfigureForContinuity(MemoryConfig config)
    {
        lock (_gate)
        {
            _continuityReadOnlyLoad = true;
            try { Configure(config); }
            finally { _continuityReadOnlyLoad = false; }
        }
    }

    private sealed class ContinuityState
    {
        [JsonRequired] public int NewEpisodesSinceConsolidation { get; set; } = default!;
        [JsonRequired] public long RecallRequests { get; set; } = default!;
        [JsonRequired] public int LastRecallCount { get; set; } = default!;
        [JsonRequired] public int LastCandidateCount { get; set; } = default!;
        [JsonRequired] public double LastBestSimilarity { get; set; } = default!;
        [JsonRequired] public long DuplicateMerges { get; set; } = default!;
        [JsonRequired] public long ForgottenEntries { get; set; } = default!;
        [JsonRequired] public long ConsolidationRuns { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        lock (_gate)
        {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            NewEpisodesSinceConsolidation = _newEpisodesSinceConsolidation,
            RecallRequests = _recallRequests,
            LastRecallCount = _lastRecallCount,
            LastCandidateCount = _lastCandidateCount,
            LastBestSimilarity = _lastBestSimilarity,
            DuplicateMerges = _duplicateMerges,
            ForgottenEntries = _forgottenEntries,
            ConsolidationRuns = _consolidationRuns
        }, RuntimeCheckpoint.Json);
        }
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        lock (_gate)
        {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _newEpisodesSinceConsolidation = v.NewEpisodesSinceConsolidation;
        _recallRequests = v.RecallRequests;
        _lastRecallCount = v.LastRecallCount;
        _lastCandidateCount = v.LastCandidateCount;
        _lastBestSimilarity = v.LastBestSimilarity;
        _duplicateMerges = v.DuplicateMerges;
        _forgottenEntries = v.ForgottenEntries;
        _consolidationRuns = v.ConsolidationRuns;
        }
    }
}
