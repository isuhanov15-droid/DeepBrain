using System.Collections.Generic;

namespace DeepBrain.Host.BrainLife;

public sealed record BrainConfig(
    WorldConfig World,
    PainConfig Pain,
    DrivesConfig Drives,
    ActionsConfig Actions,
    MoodConfig Mood,
    RewardConfig Reward,
    EpisodeConfig Episode,
    MlConfig Ml,
    CurriculumConfig Curriculum,
    EvaluationConfig Evaluation,
    Dictionary<string, ScenarioConfig> Scenarios,
    bool UseMlAdvisor = false,
    double TickRate = 5.0,
    double Epsilon = 1.0,
    double EpsilonMin = 0.05,
    double EpsilonDecay = 0.0005,
    double LoopThreshold = 0.85,
    int SelfTalkCooldown = 120,
    RewardWeightsConfig? RewardWeights = null,
    string ScenarioDefault = "calm_baseline",
    string CurriculumMode = "round_robin",
    int SelfTalkRepeatCooldownTicks = 900,
    int SelfTalkSemanticCooldownTicks = 600,
    double SelfTalkLoopMinStrength = 0.65,
    double SelfTalkRecoveryThreshold = 0.35,
    int SelfTalkLoopHoldTicks = 5,
    int SelfTalkRecoveryHoldTicks = 5,
    int SelfTalkCalmWindowHoldTicks = 3,
    MemoryConfig? Memory = null)
{
    public static BrainConfig Default => new(
        new WorldConfig(
            BaselineThreat: 0.10,
            ThreatReturnRatePerSec: 0.06,
            ShockChanceBase: 0.01,
            MicroThreatChanceBase: 0.03,
            NoveltyChanceBase: 0.02,
            SocialPingChanceBase: 0.04,
            FatigueWaveChanceBase: 0.02,
            CalmWindowChanceBase: 0.02,
            DriftRatePerSec: 0.02,
            BaselineTension: 0.25
        ),
        new PainConfig(
            BaselinePain: 0.12,
            PainReturnRatePerSec: 0.08,
            DecayPerSec: 0.04,
            ThreatK: 0.020,
            FatigueK: 0.030,
            SleepK: 0.030,
            CalmBonusPerSec: 0.03,
            SafetyBonusPerSec: 0.02
        ),
        new DrivesConfig(
            WeightSelfPreservation: 1.0,
            WeightExploration: 1.0,
            WeightAttachment: 1.0,
            WeightAgency: 1.0,
            SoftmaxTemperature: 1.0
        ),
        new ActionsConfig(
            BreatheSlow: 3,
            RestShort: 5,
            ReframeNegative: 3,
            FocusNarrow: 2,
            FocusWiden: 2,
            ExploreSignal: 2,
            EmitMessage: 60,
            EmitCooldownCalm: 60,
            EmitCooldownTender: 60,
            EmitCooldownWitty: 65,
            EmitCooldownFiery: 75
        ),
        new MoodConfig(
            CalmValenceMin: 0.05,
            CalmArousalMax: 0.30,
            CalmPainMax: 0.20,
            AnxiousPainMin: 0.35,
            AnxiousThreatIntensityMin: 0.60,
            AnxiousStressMin: 0.50,
            CuriousNoveltyMin: 0.35,
            CuriousArousalMin: 0.25,
            CuriousArousalMax: 0.45,
            TenderSocialMin: 0.35,
            TenderArousalMax: 0.35
        ),
        new RewardConfig(
            HomeostasisWeight: 1.0,
            ExploreWeight: 1.0,
            SocialWeight: 1.0,
            LoopPenaltyWeight: 0.05,
            LoopPenaltyThreshold: 0.35,
            InvalidActionPenalty: 0.05,
            ExploreBase: 0.01,
            SocialBase: 0.01
        ),
        new EpisodeConfig(
            MaxSteps: 1200,
            LoopStrengthThreshold: 0.85,
            LoopHoldTicks: 6,
            PanicSafetyMin: 0.10,
            PanicPainMin: 0.95,
            PanicThreatMin: 0.90
        ),
        new MlConfig(
            Enable: false,
            StrictRequireCore: false,
            Backend: "local",
            Remote: new MlRemoteConfig(
                Host: "127.0.0.1",
                Port: 7777,
                TimeoutMs: 2000,
                ReconnectMs: 2000
            ),
            RemoteStrict: false,
            LogBackendSwitches: true,
            Mode: "training",
            Seed: 1337,
            LearningRate: 0.0005,
            Gamma: 0.98,
            BufferSize: 20000,
            TrainEveryTicks: 10,
            BatchSize: 256,
            TrainStepsPerBatch: 1,
            NetWeightMax: 0.6,
            NetWeightWarmup: 5000,
            EpsilonStart: 1.0,
            EpsilonEnd: 0.05,
            EpsilonDecay: 0.0005,
            GradClip: 1.0,
            CheckpointPath: "checkpoints/brain_ml.chk",
            EpisodeLengthTicks: 1200,
            LoopWindow: 32,
            LoopSameK: 8,
            LoopAltK: 6,
            LoopBreakTicks: 20,
            TargetUpdateTicks: 200,
            EpsilonMin: 0.05,
            ActionMasking: true
        ),
        new CurriculumConfig(
            Mode: "round_robin",
            RewardGateThreshold: 0.05
        ),
        new EvaluationConfig(
            WindowEpisodes: 20,
            SaveReports: true
        ),
        new Dictionary<string, ScenarioConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["calm_baseline"] = new ScenarioConfig(
                BaselineThreat: 0.08,
                BaselineTension: 0.20,
                NoveltyChance: 0.02,
                SocialPingChance: 0.03,
                FatigueWaveChance: 0.02,
                CalmWindowChance: 0.03,
                DriftRate: 0.015,
                ShockChance: 0.006,
                EpisodeLengthMultiplier: 1.0
            ),
            ["novelty_walk"] = new ScenarioConfig(
                BaselineThreat: 0.08,
                BaselineTension: 0.22,
                NoveltyChance: 0.05,
                SocialPingChance: 0.02,
                FatigueWaveChance: 0.02,
                CalmWindowChance: 0.04,
                DriftRate: 0.02,
                ShockChance: 0.006,
                EpisodeLengthMultiplier: 1.0
            ),
            ["threat_pulses"] = new ScenarioConfig(
                BaselineThreat: 0.16,
                BaselineTension: 0.35,
                NoveltyChance: 0.02,
                SocialPingChance: 0.02,
                FatigueWaveChance: 0.03,
                CalmWindowChance: 0.015,
                DriftRate: 0.025,
                ShockChance: 0.02,
                EpisodeLengthMultiplier: 1.0
            ),
            ["social_pull"] = new ScenarioConfig(
                BaselineThreat: 0.10,
                BaselineTension: 0.24,
                NoveltyChance: 0.02,
                SocialPingChance: 0.06,
                FatigueWaveChance: 0.02,
                CalmWindowChance: 0.03,
                DriftRate: 0.02,
                ShockChance: 0.008,
                EpisodeLengthMultiplier: 1.0
            ),
            ["fatigue_day"] = new ScenarioConfig(
                BaselineThreat: 0.10,
                BaselineTension: 0.28,
                NoveltyChance: 0.02,
                SocialPingChance: 0.03,
                FatigueWaveChance: 0.05,
                CalmWindowChance: 0.02,
                DriftRate: 0.02,
                ShockChance: 0.01,
                EpisodeLengthMultiplier: 1.0
            ),
            ["mixed_adaptive"] = new ScenarioConfig(
                BaselineThreat: 0.10,
                BaselineTension: 0.25,
                NoveltyChance: 0.03,
                SocialPingChance: 0.04,
                FatigueWaveChance: 0.03,
                CalmWindowChance: 0.03,
                DriftRate: 0.02,
                ShockChance: 0.012,
                EpisodeLengthMultiplier: 1.0
            )
        },
        UseMlAdvisor: false,
        TickRate: 5.0,
        Epsilon: 1.0,
        EpsilonMin: 0.05,
        EpsilonDecay: 0.0005,
        LoopThreshold: 0.85,
        SelfTalkCooldown: 40,
        RewardWeights: new RewardWeightsConfig(
            Homeostasis: 1.0,
            Explore: 1.0,
            Social: 1.0,
            LoopPenalty: 0.05,
            InvalidActionPenalty: 0.05
        ),
        ScenarioDefault: "calm_baseline",
        CurriculumMode: "round_robin",
        SelfTalkRepeatCooldownTicks: 300,
        SelfTalkSemanticCooldownTicks: 180,
        SelfTalkLoopMinStrength: 0.65,
        SelfTalkRecoveryThreshold: 0.35,
        SelfTalkLoopHoldTicks: 3,
        SelfTalkRecoveryHoldTicks: 3,
        SelfTalkCalmWindowHoldTicks: 3,
        Memory: MemoryConfig.Default
    );
}

public sealed record WorldConfig(
    double BaselineThreat,
    double ThreatReturnRatePerSec,
    double ShockChanceBase,
    double MicroThreatChanceBase,
    double NoveltyChanceBase,
    double SocialPingChanceBase,
    double FatigueWaveChanceBase,
    double CalmWindowChanceBase,
    double DriftRatePerSec,
    double BaselineTension
);

public sealed record PainConfig(
    double BaselinePain,
    double PainReturnRatePerSec,
    double DecayPerSec,
    double ThreatK,
    double FatigueK,
    double SleepK,
    double CalmBonusPerSec,
    double SafetyBonusPerSec
);

public sealed record DrivesConfig(
    double WeightSelfPreservation,
    double WeightExploration,
    double WeightAttachment,
    double WeightAgency,
    double SoftmaxTemperature
);

public sealed record ActionsConfig(
    int BreatheSlow,
    int RestShort,
    int ReframeNegative,
    int FocusNarrow,
    int FocusWiden,
    int ExploreSignal,
    int EmitMessage,
    int EmitCooldownCalm,
    int EmitCooldownTender,
    int EmitCooldownWitty,
    int EmitCooldownFiery
);

public sealed record MoodConfig(
    double CalmValenceMin,
    double CalmArousalMax,
    double CalmPainMax,
    double AnxiousPainMin,
    double AnxiousThreatIntensityMin,
    double AnxiousStressMin,
    double CuriousNoveltyMin,
    double CuriousArousalMin,
    double CuriousArousalMax,
    double TenderSocialMin,
    double TenderArousalMax
);

public sealed record RewardConfig(
    double HomeostasisWeight,
    double ExploreWeight,
    double SocialWeight,
    double LoopPenaltyWeight,
    double LoopPenaltyThreshold,
    double InvalidActionPenalty,
    double ExploreBase,
    double SocialBase
);

public sealed record RewardWeightsConfig(
    double Homeostasis,
    double Explore,
    double Social,
    double LoopPenalty,
    double InvalidActionPenalty
);

public sealed record EpisodeConfig(
    int MaxSteps,
    double LoopStrengthThreshold,
    int LoopHoldTicks,
    double PanicSafetyMin,
    double PanicPainMin,
    double PanicThreatMin
);

public sealed record MlRemoteConfig(
    string Host,
    int Port,
    int TimeoutMs,
    int ReconnectMs
);

public sealed record MlConfig(
    bool Enable,
    bool StrictRequireCore,
    string Backend,
    MlRemoteConfig Remote,
    bool RemoteStrict,
    bool LogBackendSwitches,
    string Mode,
    int Seed,
    double LearningRate,
    double Gamma,
    int BufferSize,
    int TrainEveryTicks,
    int BatchSize,
    int TrainStepsPerBatch,
    double NetWeightMax,
    double NetWeightWarmup,
    double EpsilonStart,
    double EpsilonEnd,
    double EpsilonDecay,
    double GradClip,
    string CheckpointPath,
    int EpisodeLengthTicks,
    int LoopWindow,
    int LoopSameK,
    int LoopAltK,
    int LoopBreakTicks,
    int TargetUpdateTicks,
    double EpsilonMin,
    bool ActionMasking
);

public sealed record CurriculumConfig(
    string Mode,
    double RewardGateThreshold
);

public sealed record EvaluationConfig(
    int WindowEpisodes,
    bool SaveReports
);

public sealed record MemoryConfig(
    bool Enable,
    string Path,
    int MaxEpisodes,
    int RecallTopK,
    double MinSimilarity,
    double MaxActionBias,
    double RecencyHalfLifeDays,
    int MinEpisodesBeforeBias,
    int MinEpisodeSteps,
    double RewardScale)
{
    public static MemoryConfig Default => new(
        Enable: true,
        Path: "memory/episodes.jsonl",
        MaxEpisodes: 5000,
        RecallTopK: 8,
        MinSimilarity: 0.45,
        MaxActionBias: 0.15,
        RecencyHalfLifeDays: 90,
        MinEpisodesBeforeBias: 3,
        MinEpisodeSteps: 25,
        RewardScale: 0.02
    );
}

public sealed record ScenarioConfig(
    double BaselineThreat,
    double BaselineTension,
    double NoveltyChance,
    double SocialPingChance,
    double FatigueWaveChance,
    double CalmWindowChance,
    double DriftRate,
    double ShockChance,
    double EpisodeLengthMultiplier
);
