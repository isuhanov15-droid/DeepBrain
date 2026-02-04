namespace DeepBrain.Host.BrainLife;

public sealed record BrainConfig(
    WorldConfig World,
    PainConfig Pain,
    DrivesConfig Drives,
    ActionsConfig Actions,
    MoodConfig Mood,
    MlConfig Ml,
    bool UseMlAdvisor = false)
{
    public static BrainConfig Default => new(
        new WorldConfig(
            BaselineThreat: 0.10,
            ThreatReturnRatePerSec: 0.06,
            ShockChanceBase: 0.01,
            MicroThreatChanceBase: 0.03,
            NoveltyChanceBase: 0.02,
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
        )
    );
}

public sealed record WorldConfig(
    double BaselineThreat,
    double ThreatReturnRatePerSec,
    double ShockChanceBase,
    double MicroThreatChanceBase,
    double NoveltyChanceBase,
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
