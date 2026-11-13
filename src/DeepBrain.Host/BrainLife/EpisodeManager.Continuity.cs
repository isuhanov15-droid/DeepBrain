// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class EpisodeManager
{
    private sealed class ContinuityState
    {
        [JsonRequired] public int EpisodeId { get; set; } = default!;
        [JsonRequired] public int EpisodeTick { get; set; } = default!;
        [JsonRequired] public int MaxSteps { get; set; } = default!;
        [JsonRequired] public double LoopStrengthThreshold { get; set; } = default!;
        [JsonRequired] public int LoopHoldTicks { get; set; } = default!;
        [JsonRequired] public int LoopHoldCount { get; set; } = default!;
        [JsonRequired] public double PanicSafetyMin { get; set; } = default!;
        [JsonRequired] public double PanicPainMin { get; set; } = default!;
        [JsonRequired] public double PanicThreatMin { get; set; } = default!;
        [JsonRequired] public int PanicHoldTicks { get; set; } = default!;
        [JsonRequired] public int PanicHoldCount { get; set; } = default!;
        [JsonRequired] public string LastResetReason { get; set; } = default!;
        [JsonRequired] public string? RequestedResetReason { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            EpisodeId = _episodeId,
            EpisodeTick = _episodeTick,
            MaxSteps = _maxSteps,
            LoopStrengthThreshold = _loopStrengthThreshold,
            LoopHoldTicks = _loopHoldTicks,
            LoopHoldCount = _loopHoldCount,
            PanicSafetyMin = _panicSafetyMin,
            PanicPainMin = _panicPainMin,
            PanicThreatMin = _panicThreatMin,
            PanicHoldTicks = _panicHoldTicks,
            PanicHoldCount = _panicHoldCount,
            LastResetReason = _lastResetReason,
            RequestedResetReason = _requestedResetReason
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);
        if (v.EpisodeId < 0 || v.EpisodeTick < 0 || v.MaxSteps < 1 || v.EpisodeTick > v.MaxSteps) throw new InvalidDataException("Invalid episode position");
        _episodeId = v.EpisodeId;
        _episodeTick = v.EpisodeTick;
        _maxSteps = v.MaxSteps;
        _loopStrengthThreshold = v.LoopStrengthThreshold;
        _loopHoldTicks = v.LoopHoldTicks;
        _loopHoldCount = v.LoopHoldCount;
        _panicSafetyMin = v.PanicSafetyMin;
        _panicPainMin = v.PanicPainMin;
        _panicThreatMin = v.PanicThreatMin;
        _panicHoldTicks = v.PanicHoldTicks;
        _panicHoldCount = v.PanicHoldCount;
        _lastResetReason = v.LastResetReason;
        _requestedResetReason = v.RequestedResetReason;
    }
}
