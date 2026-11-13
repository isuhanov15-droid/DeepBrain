// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Linq;
using System.IO;
using System.Globalization;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Host.Cognition;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V5;
using DeepBrain.Shared.BrainDtos.V6;
using DeepBrain.Shared.Localization;
using DeepBrain.Shared.Trace;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class LifeLoop
{
    private sealed class ContinuityState
    {
        [JsonRequired] public PendingLearningTransition? PendingMlTransition { get; set; } = default!;
        [JsonRequired] public Dictionary<string, int> ActionCounts { get; set; } = default!;
        [JsonRequired] public Dictionary<string, int> EventCounts { get; set; } = default!;
        [JsonRequired] public int StatsTicks { get; set; } = default!;
        [JsonRequired] public double SumPain { get; set; } = default!;
        [JsonRequired] public double SumSafety { get; set; } = default!;
        [JsonRequired] public double SumArousal { get; set; } = default!;
        [JsonRequired] public double SumReward { get; set; } = default!;
        [JsonRequired] public double AvgReward200 { get; set; } = default!;
        [JsonRequired] public double SumThreat { get; set; } = default!;
        [JsonRequired] public double SumCalm { get; set; } = default!;
        [JsonRequired] public double SumStress { get; set; } = default!;
        [JsonRequired] public double SumThreatFocus { get; set; } = default!;
        [JsonRequired] public int PainClampedCount { get; set; } = default!;
        [JsonRequired] public List<double> PainSamples { get; set; } = default!;
        [JsonRequired] public DateTimeOffset EpisodeStartTs { get; set; } = default!;
        [JsonRequired] public int EpisodeSteps { get; set; } = default!;
        [JsonRequired] public double EpisodeRewardSum { get; set; } = default!;
        [JsonRequired] public RewardDto EpisodeRewardBreakdown { get; set; } = default!;
        [JsonRequired] public Dictionary<string, int> EpisodeActionCounts { get; set; } = default!;
        [JsonRequired] public Dictionary<string, double> EpisodeActionRewardSums { get; set; } = default!;
        [JsonRequired] public Dictionary<string, int> EpisodeMoodCounts { get; set; } = default!;
        [JsonRequired] public int EpisodeLoopCount { get; set; } = default!;
        [JsonRequired] public bool EpisodeLoopActive { get; set; } = default!;
        [JsonRequired] public double EpisodeMaxLoopStrength { get; set; } = default!;
        [JsonRequired] public double EpisodeSumPain { get; set; } = default!;
        [JsonRequired] public double EpisodeMaxPain { get; set; } = default!;
        [JsonRequired] public double EpisodeSumSafety { get; set; } = default!;
        [JsonRequired] public double EpisodeSumArousal { get; set; } = default!;
        [JsonRequired] public int EpisodeSocialSignals { get; set; } = default!;
        [JsonRequired] public int EpisodeSelfTalkCount { get; set; } = default!;
        [JsonRequired] public int EpisodeMaskFallbackCount { get; set; } = default!;
        [JsonRequired] public int EpisodeInvalidActionCount { get; set; } = default!;
        [JsonRequired] public bool EpisodeMlUsed { get; set; } = default!;
        [JsonRequired] public double EpisodeLastEpsilon { get; set; } = default!;
        [JsonRequired] public string MlModeOverride { get; set; } = default!;
        [JsonRequired] public int TrainingEpisodeCount { get; set; } = default!;
        [JsonRequired] public int EvalEpisodeCount { get; set; } = default!;
        [JsonRequired] public bool SleepConsolidated { get; set; } = default!;
        [JsonRequired] public string? LastSelfTalk { get; set; } = default!;
        [JsonRequired] public int LoopHighTicks { get; set; } = default!;
        [JsonRequired] public int LoopLowTicks { get; set; } = default!;
        [JsonRequired] public int CalmWindowTicks { get; set; } = default!;
        [JsonRequired] public int CalmWindowInactiveTicks { get; set; } = default!;
        [JsonRequired] public bool CalmWindowOpen { get; set; } = default!;
        [JsonRequired] public bool SelfTalkLoopActive { get; set; } = default!;
        [JsonRequired] public LifeStatsDto StatsSnapshot { get; set; } = default!;
        [JsonRequired] public string? ActiveHabitId { get; set; } = default!;
        [JsonRequired] public string ActiveHabitCue { get; set; } = default!;
        [JsonRequired] public double ActiveHabitInfluence { get; set; } = default!;
        [JsonRequired] public HomeostasisDto Homeo { get; set; } = default!;
        [JsonRequired] public AffectDto Affect { get; set; } = default!;
        [JsonRequired] public long Tick { get; set; } = default!;
        [JsonRequired] public bool Running { get; set; } = default!;
        [JsonRequired] public bool StepRequested { get; set; } = default!;
        [JsonRequired] public double AgencyOffset { get; set; } = default!;
        [JsonRequired] public string? OutputSinceDiag { get; set; } = default!;
        [JsonRequired] public int LoopBreakTicks { get; set; } = default!;
        [JsonRequired] public string ScenarioName { get; set; } = default!;
        [JsonRequired] public string CurriculumMode { get; set; } = default!;
        [JsonRequired] public int ScenarioIndex { get; set; } = default!;
        [JsonRequired] public string CurriculumModeOverride { get; set; } = default!;
        [JsonRequired] public List<EpisodeDto> Episodes { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            PendingMlTransition = _pendingMlTransition,
            ActionCounts = _actionCounts,
            EventCounts = _eventCounts,
            StatsTicks = _statsTicks,
            SumPain = _sumPain,
            SumSafety = _sumSafety,
            SumArousal = _sumArousal,
            SumReward = _sumReward,
            AvgReward200 = _avgReward200,
            SumThreat = _sumThreat,
            SumCalm = _sumCalm,
            SumStress = _sumStress,
            SumThreatFocus = _sumThreatFocus,
            PainClampedCount = _painClampedCount,
            PainSamples = _painSamples,
            EpisodeStartTs = _episodeStartTs,
            EpisodeSteps = _episodeSteps,
            EpisodeRewardSum = _episodeRewardSum,
            EpisodeRewardBreakdown = _episodeRewardBreakdown,
            EpisodeActionCounts = _episodeActionCounts,
            EpisodeActionRewardSums = _episodeActionRewardSums,
            EpisodeMoodCounts = _episodeMoodCounts,
            EpisodeLoopCount = _episodeLoopCount,
            EpisodeLoopActive = _episodeLoopActive,
            EpisodeMaxLoopStrength = _episodeMaxLoopStrength,
            EpisodeSumPain = _episodeSumPain,
            EpisodeMaxPain = _episodeMaxPain,
            EpisodeSumSafety = _episodeSumSafety,
            EpisodeSumArousal = _episodeSumArousal,
            EpisodeSocialSignals = _episodeSocialSignals,
            EpisodeSelfTalkCount = _episodeSelfTalkCount,
            EpisodeMaskFallbackCount = _episodeMaskFallbackCount,
            EpisodeInvalidActionCount = _episodeInvalidActionCount,
            EpisodeMlUsed = _episodeMlUsed,
            EpisodeLastEpsilon = _episodeLastEpsilon,
            MlModeOverride = _mlModeOverride,
            TrainingEpisodeCount = _trainingEpisodeCount,
            EvalEpisodeCount = _evalEpisodeCount,
            SleepConsolidated = _sleepConsolidated,
            LastSelfTalk = _lastSelfTalk,
            LoopHighTicks = _loopHighTicks,
            LoopLowTicks = _loopLowTicks,
            CalmWindowTicks = _calmWindowTicks,
            CalmWindowInactiveTicks = _calmWindowInactiveTicks,
            CalmWindowOpen = _calmWindowOpen,
            SelfTalkLoopActive = _selfTalkLoopActive,
            StatsSnapshot = _statsSnapshot,
            ActiveHabitId = _activeHabitId,
            ActiveHabitCue = _activeHabitCue,
            ActiveHabitInfluence = _activeHabitInfluence,
            Homeo = _homeo,
            Affect = _affect,
            Tick = _tick,
            Running = _running,
            StepRequested = _stepRequested,
            AgencyOffset = _agencyOffset,
            OutputSinceDiag = _outputSinceDiag,
            LoopBreakTicks = _loopBreakTicks,
            ScenarioName = _scenarioName,
            CurriculumMode = _curriculumMode,
            ScenarioIndex = _scenarioIndex,
            CurriculumModeOverride = _curriculumModeOverride,
            Episodes = _episodes
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _pendingMlTransition = v.PendingMlTransition;
        _actionCounts.Clear();
        foreach (var pair in v.ActionCounts) _actionCounts.Add(pair.Key, pair.Value);
        _eventCounts.Clear();
        foreach (var pair in v.EventCounts) _eventCounts.Add(pair.Key, pair.Value);
        _statsTicks = v.StatsTicks;
        _sumPain = v.SumPain;
        _sumSafety = v.SumSafety;
        _sumArousal = v.SumArousal;
        _sumReward = v.SumReward;
        _avgReward200 = v.AvgReward200;
        _sumThreat = v.SumThreat;
        _sumCalm = v.SumCalm;
        _sumStress = v.SumStress;
        _sumThreatFocus = v.SumThreatFocus;
        _painClampedCount = v.PainClampedCount;
        _painSamples.Clear();
        _painSamples.AddRange(v.PainSamples);
        _episodeStartTs = v.EpisodeStartTs;
        _episodeSteps = v.EpisodeSteps;
        _episodeRewardSum = v.EpisodeRewardSum;
        _episodeRewardBreakdown = v.EpisodeRewardBreakdown;
        _episodeActionCounts.Clear();
        foreach (var pair in v.EpisodeActionCounts) _episodeActionCounts.Add(pair.Key, pair.Value);
        _episodeActionRewardSums.Clear();
        foreach (var pair in v.EpisodeActionRewardSums) _episodeActionRewardSums.Add(pair.Key, pair.Value);
        _episodeMoodCounts.Clear();
        foreach (var pair in v.EpisodeMoodCounts) _episodeMoodCounts.Add(pair.Key, pair.Value);
        _episodeLoopCount = v.EpisodeLoopCount;
        _episodeLoopActive = v.EpisodeLoopActive;
        _episodeMaxLoopStrength = v.EpisodeMaxLoopStrength;
        _episodeSumPain = v.EpisodeSumPain;
        _episodeMaxPain = v.EpisodeMaxPain;
        _episodeSumSafety = v.EpisodeSumSafety;
        _episodeSumArousal = v.EpisodeSumArousal;
        _episodeSocialSignals = v.EpisodeSocialSignals;
        _episodeSelfTalkCount = v.EpisodeSelfTalkCount;
        _episodeMaskFallbackCount = v.EpisodeMaskFallbackCount;
        _episodeInvalidActionCount = v.EpisodeInvalidActionCount;
        _episodeMlUsed = v.EpisodeMlUsed;
        _episodeLastEpsilon = v.EpisodeLastEpsilon;
        _mlModeOverride = v.MlModeOverride;
        _trainingEpisodeCount = v.TrainingEpisodeCount;
        _evalEpisodeCount = v.EvalEpisodeCount;
        _sleepConsolidated = v.SleepConsolidated;
        _lastSelfTalk = v.LastSelfTalk;
        _loopHighTicks = v.LoopHighTicks;
        _loopLowTicks = v.LoopLowTicks;
        _calmWindowTicks = v.CalmWindowTicks;
        _calmWindowInactiveTicks = v.CalmWindowInactiveTicks;
        _calmWindowOpen = v.CalmWindowOpen;
        _selfTalkLoopActive = v.SelfTalkLoopActive;
        _statsSnapshot = v.StatsSnapshot;
        _activeHabitId = v.ActiveHabitId;
        _activeHabitCue = v.ActiveHabitCue;
        _activeHabitInfluence = v.ActiveHabitInfluence;
        _homeo = v.Homeo;
        _affect = v.Affect;
        _tick = v.Tick;
        _running = v.Running;
        _stepRequested = v.StepRequested;
        _agencyOffset = v.AgencyOffset;
        _outputSinceDiag = v.OutputSinceDiag;
        _loopBreakTicks = v.LoopBreakTicks;
        _scenarioName = v.ScenarioName;
        _curriculumMode = v.CurriculumMode;
        _scenarioIndex = v.ScenarioIndex;
        _curriculumModeOverride = v.CurriculumModeOverride;
        _episodes.Clear();
        _episodes.AddRange(v.Episodes);
    }
}
