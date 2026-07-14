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
    MemoryConfig? Memory = null,
    LlmConfig? Llm = null,
    ExternalApiConfig? ExternalApi = null)
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
        )
        {
            PanicHoldTicks = 5,
            PanicRecoverySafety = 0.35,
            PanicRecoveryPainMax = 0.70,
            PanicTerminalPenalty = 0.50
        },
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
        Memory: MemoryConfig.Default,
        Llm: LlmConfig.Default,
        ExternalApi: ExternalApiConfig.Default
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
)
{
    // These init properties preserve compatibility with older brainconfig.json
    // files while allowing panic handling to be tuned independently.
    public int PanicHoldTicks { get; init; } = 5;
    public double PanicRecoverySafety { get; init; } = 0.35;
    public double PanicRecoveryPainMax { get; init; } = 0.70;
    public double PanicTerminalPenalty { get; init; } = 0.50;
}

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
    // Memory v2 settings are init properties so v1.1 configuration files and
    // existing constructor calls remain source- and JSON-compatible.
    public double DeduplicationSimilarity { get; init; } = 0.96;
    public double ConsolidationSimilarity { get; init; } = 0.90;
    public int ConsolidateEveryEpisodes { get; init; } = 50;
    public double ForgetAfterDays { get; init; } = 180;
    public double ForgetThreshold { get; init; } = 0.12;
    public double RepeatBoost { get; init; } = 0.08;
    public int ExperienceMinOccurrences { get; init; } = 3;
    public double ExperienceWeight { get; init; } = 0.35;
    public int MaxIndexedCandidates { get; init; } = 512;

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
    )
    {
        DeduplicationSimilarity = 0.96,
        ConsolidationSimilarity = 0.90,
        ConsolidateEveryEpisodes = 50,
        ForgetAfterDays = 180,
        ForgetThreshold = 0.12,
        RepeatBoost = 0.08,
        ExperienceMinOccurrences = 3,
        ExperienceWeight = 0.35,
        MaxIndexedCandidates = 512
    };
}

public sealed record LlmConfig
{
    public bool Enable { get; init; }
    public string Provider { get; init; } = "ollama";
    public string BaseUrl { get; init; } = "http://127.0.0.1:11434/";
    public string Model { get; init; } = "qwen3.5:4b-q4_K_M";
    public bool AutoObserve { get; init; }
    public int ObserveEveryTicks { get; init; } = 300;
    public int TimeoutSeconds { get; init; } = 120;
    public int MaxStalenessTicks { get; init; } = 600;
    public int NumPredict { get; init; } = 128;
    public double Temperature { get; init; } = 0.1;
    public string KeepAlive { get; init; } = "5m";
    public int MaxMemoryLines { get; init; } = 2;
    public int MaxRecentEvents { get; init; } = 2;
    public int MaxContextChars { get; init; } = 96;

    public static LlmConfig Default => new();

    public LlmConfig Normalize() => this with
    {
        Provider = string.IsNullOrWhiteSpace(Provider) ? "ollama" : Provider.Trim().ToLowerInvariant(),
        BaseUrl = NormalizeBaseUrl(BaseUrl),
        Model = string.IsNullOrWhiteSpace(Model) ? "qwen3.5:4b-q4_K_M" : Model.Trim(),
        ObserveEveryTicks = Math.Clamp(ObserveEveryTicks, 10, 1_000_000),
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 600),
        MaxStalenessTicks = Math.Clamp(MaxStalenessTicks, 1, 100_000),
        NumPredict = Math.Clamp(NumPredict, 32, 1024),
        Temperature = Math.Clamp(double.IsFinite(Temperature) ? Temperature : 0.1, 0, 1),
        KeepAlive = string.IsNullOrWhiteSpace(KeepAlive) ? "5m" : KeepAlive.Trim(),
        MaxMemoryLines = Math.Clamp(MaxMemoryLines, 0, 8),
        MaxRecentEvents = Math.Clamp(MaxRecentEvents, 0, 12),
        MaxContextChars = Math.Clamp(MaxContextChars, 40, 400)
    };

    private static string NormalizeBaseUrl(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "http://127.0.0.1:11434/"
            : value.Trim();
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }
}

public sealed record ExternalApiConfig
{
    public bool Enable { get; init; }
    public string Prefix { get; init; } = "http://127.0.0.1:8787/";
    public bool RequireToken { get; init; }
    public string TokenEnvironmentVariable { get; init; } = "DEEPBRAIN_API_TOKEN";
    public int StatePublishEveryTicks { get; init; } = 5;
    public string[] AllowedOrigins { get; init; } = Array.Empty<string>();
    public int SubscriberBufferCapacity { get; init; } = 128;

    public static ExternalApiConfig Default => new();

    public ExternalApiConfig Normalize()
    {
        var prefix = string.IsNullOrWhiteSpace(Prefix)
            ? "http://127.0.0.1:8787/"
            : Prefix.Trim();
        if (!prefix.EndsWith('/'))
            prefix += "/";

        return this with
        {
            Prefix = prefix,
            TokenEnvironmentVariable = string.IsNullOrWhiteSpace(TokenEnvironmentVariable)
                ? "DEEPBRAIN_API_TOKEN"
                : TokenEnvironmentVariable.Trim(),
            StatePublishEveryTicks = Math.Clamp(StatePublishEveryTicks, 1, 10_000),
            AllowedOrigins = (AllowedOrigins ?? Array.Empty<string>())
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Select(origin => origin.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SubscriberBufferCapacity = Math.Clamp(SubscriberBufferCapacity, 8, 4096)
        };
    }
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
