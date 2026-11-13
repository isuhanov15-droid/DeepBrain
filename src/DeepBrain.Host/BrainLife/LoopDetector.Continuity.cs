// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class LoopDetector
{
    private sealed class ContinuityState
    {
        [JsonRequired] public Queue<double> RecentRewards { get; set; } = default!;
        [JsonRequired] public Queue<string> RecentActions { get; set; } = default!;
        [JsonRequired] public Queue<string> Fingerprints { get; set; } = default!;
        [JsonRequired] public Queue<string> StateKeys { get; set; } = default!;
        [JsonRequired] public int Window { get; set; } = default!;
        [JsonRequired] public int SameK { get; set; } = default!;
        [JsonRequired] public int AltK { get; set; } = default!;
        [JsonRequired] public bool InLoop { get; set; } = default!;
        [JsonRequired] public long LastLoopAnnounceTick { get; set; } = default!;
        [JsonRequired] public string LastFingerprint { get; set; } = default!;
        [JsonRequired] public string LastStateKey { get; set; } = default!;
        [JsonRequired] public int RepeatStreak { get; set; } = default!;
        [JsonRequired] public int StateStreak { get; set; } = default!;
        [JsonRequired] public string LastAction { get; set; } = default!;
        [JsonRequired] public int SameActionStreak { get; set; } = default!;
        [JsonRequired] public double AvgRewardShort { get; set; } = default!;
        [JsonRequired] public int LoopObservedCount { get; set; } = default!;
        [JsonRequired] public double LoopPenalty { get; set; } = default!;
        [JsonRequired] public string LoopType { get; set; } = default!;
        [JsonRequired] public bool IsLoopDetected { get; set; } = default!;
        [JsonRequired] public bool IsInLoop { get; set; } = default!;
        [JsonRequired] public bool IsHeavyLoop { get; set; } = default!;
        [JsonRequired] public double LoopStrength { get; set; } = default!;
        [JsonRequired] public int Streak { get; set; } = default!;
        [JsonRequired] public bool EnteredLoop { get; set; } = default!;
        [JsonRequired] public bool LoopTypeChanged { get; set; } = default!;
        [JsonRequired] public bool RecoveredFromLoop { get; set; } = default!;
        [JsonRequired] public bool LoopEscalated { get; set; } = default!;
        [JsonRequired] public bool IsProgressStalled { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            RecentRewards = _recentRewards,
            RecentActions = _recentActions,
            Fingerprints = _fingerprints,
            StateKeys = _stateKeys,
            Window = _window,
            SameK = _sameK,
            AltK = _altK,
            InLoop = _inLoop,
            LastLoopAnnounceTick = _lastLoopAnnounceTick,
            LastFingerprint = _lastFingerprint,
            LastStateKey = _lastStateKey,
            RepeatStreak = _repeatStreak,
            StateStreak = _stateStreak,
            LastAction = LastAction,
            SameActionStreak = SameActionStreak,
            AvgRewardShort = AvgRewardShort,
            LoopObservedCount = LoopObservedCount,
            LoopPenalty = LoopPenalty,
            LoopType = LoopType,
            IsLoopDetected = IsLoopDetected,
            IsInLoop = IsInLoop,
            IsHeavyLoop = IsHeavyLoop,
            LoopStrength = LoopStrength,
            Streak = Streak,
            EnteredLoop = EnteredLoop,
            LoopTypeChanged = LoopTypeChanged,
            RecoveredFromLoop = RecoveredFromLoop,
            LoopEscalated = LoopEscalated,
            IsProgressStalled = IsProgressStalled
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _recentRewards.Clear();
        foreach (var item in v.RecentRewards) _recentRewards.Enqueue(item);
        _recentActions.Clear();
        foreach (var item in v.RecentActions) _recentActions.Enqueue(item);
        _fingerprints.Clear();
        foreach (var item in v.Fingerprints) _fingerprints.Enqueue(item);
        _stateKeys.Clear();
        foreach (var item in v.StateKeys) _stateKeys.Enqueue(item);
        _window = v.Window;
        _sameK = v.SameK;
        _altK = v.AltK;
        _inLoop = v.InLoop;
        _lastLoopAnnounceTick = v.LastLoopAnnounceTick;
        _lastFingerprint = v.LastFingerprint;
        _lastStateKey = v.LastStateKey;
        _repeatStreak = v.RepeatStreak;
        _stateStreak = v.StateStreak;
        LastAction = v.LastAction;
        SameActionStreak = v.SameActionStreak;
        AvgRewardShort = v.AvgRewardShort;
        LoopObservedCount = v.LoopObservedCount;
        LoopPenalty = v.LoopPenalty;
        LoopType = v.LoopType;
        IsLoopDetected = v.IsLoopDetected;
        IsInLoop = v.IsInLoop;
        IsHeavyLoop = v.IsHeavyLoop;
        LoopStrength = v.LoopStrength;
        Streak = v.Streak;
        EnteredLoop = v.EnteredLoop;
        LoopTypeChanged = v.LoopTypeChanged;
        RecoveredFromLoop = v.RecoveredFromLoop;
        LoopEscalated = v.LoopEscalated;
        IsProgressStalled = v.IsProgressStalled;
    }
}
