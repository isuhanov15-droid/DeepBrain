using System.Linq;
using System.IO;
using System.Globalization;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V5;
using DeepBrain.Shared.BrainDtos.V6;
using DeepBrain.Shared.Localization;
using DeepBrain.Shared.Trace;

namespace DeepBrain.Host.BrainLife;

public sealed class LifeLoop
{
    private readonly WorldSim _world;
    private readonly HomeostasisEngine _homeostasis;
    private readonly InstinctEngine _instincts;
    private readonly EmotionEngine _emotion;
    private readonly ActionSelector _selector;
    private readonly Actuator _actuator;
    private readonly RewardEngine _reward;
    private readonly RewardCalculator _rewardCalc;
    private readonly LearningEngine _learning;
    private readonly Action<LifeStateDto, CancellationToken> _broadcast;
    private readonly Action<TraceDto, CancellationToken> _trace;
    private readonly Action<LifeOutputDto, CancellationToken> _output;
    private readonly Action<string> _log;
    private readonly BrainConfigLoader _configLoader;
    private readonly EpisodeMemory _memory = new();
    private readonly PersistentEpisodicMemory _longTermMemory;
    private readonly LoopDetector _loop = new();
    private readonly EpisodeManager _episode;
    private readonly MoodInertiaEngine _inertia = new();
    private readonly DominantDriveResolver _driveResolver = new();
    private readonly CircadianClock _clock = new();
    private readonly SleepEngine _sleep = new();
    private readonly GoalResolver _goals = new();
    private readonly PlanEngine _plan = new();
    private readonly AttentionEngine _attention = new();
    private readonly AttentionInertiaEngine _attentionInertia = new();
    private readonly SemanticMemory _semantic = new();
    private readonly SelfTalkEngine _selfTalk = new();
    private readonly SelfTalkThrottle _selfTalkThrottle = new();
    private readonly ActionCooldowns _cooldowns = new();
    private readonly PersonalityProfile _personality = PersonalityProfile.LoadLada();
    private readonly VoiceModeResolver _voice = new();
    private readonly HabitSystem _habits = new();
    private readonly MessageDedupeGuard _dedupe = new();
    private readonly CalmBaselineEngine _calmBaseline = new();
    private readonly AppraisalEngine _appraisal = new();
    private readonly ActionMasker _masker = new();
    private readonly CurriculumManager _curriculum;
    private readonly ScenarioScorer _scenarioScorer = new();
    private readonly PolicyEvaluator _policyEvaluator;
    private readonly EpisodeReportWriter _reportWriter = new();
    private readonly IMlPolicyAdvisor _ml;
    private readonly Dictionary<string, int> _actionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _eventCounts = new(StringComparer.Ordinal);
    private int _anxiousCount;
    private int _calmCount;
    private int _curiousCount;
    private int _neutralCount;
    private int _tenderCount;
    private int _statsTicks;
    private double _sumPain;
    private double _sumSafety;
    private double _sumArousal;
    private double _sumReward;
    private double _avgReward200;
    private double _sumThreat;
    private double _sumCalm;
    private double _sumStress;
    private double _sumThreatFocus;
    private int _painClampedCount;
    private readonly List<double> _painSamples = new(256);
    private DateTimeOffset _episodeStartTs = DateTimeOffset.Now;
    private int _episodeSteps;
    private double _episodeRewardSum;
    private RewardDto _episodeRewardBreakdown = new(0, 0, 0, 0, 0, 0);
    private readonly Dictionary<string, int> _episodeActionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _episodeActionRewardSums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _episodeMoodCounts = new(StringComparer.OrdinalIgnoreCase);
    private int _episodeLoopCount;
    private bool _episodeLoopActive;
    private double _episodeMaxLoopStrength;
    private double _episodeSumPain;
    private double _episodeMaxPain;
    private double _episodeSumSafety;
    private double _episodeSumArousal;
    private int _episodeSocialSignals;
    private int _episodeSelfTalkCount;
    private int _episodeMaskFallbackCount;
    private int _episodeInvalidActionCount;
    private bool _episodeMlUsed;
    private double _episodeLastEpsilon;
    private string _mlModeOverride = "";
    private int _trainingEpisodeCount;
    private int _evalEpisodeCount;
    private bool _sleepConsolidated;
    private string? _lastSelfTalk;
    private int _loopHighTicks;
    private int _loopLowTicks;
    private int _calmWindowTicks;
    private int _calmWindowInactiveTicks;
    private bool _calmWindowOpen;
    private bool _selfTalkLoopActive;
    private LifeStatsDto _statsSnapshot = new(0, 0, 0, 0);
    private string _configVersion = "default";
    private MlPolicyDto? _mlTelemetry;
    private bool _mlCoreMissingLogged;
    private bool _mlLoaded;

    private HomeostasisDto _homeo = new(0.7, 0.2, 0.3, 0.1, 0.7);
    private AffectDto _affect = new("calm", 0.2, 0.3);
    private long _tick;
    private bool _running = true;
    private bool _stepRequested;
    private double _agencyOffset;
    private string? _outputSinceDiag;
    private int _loopBreakTicks;
    private string _scenarioName = "calm_baseline";
    private string _curriculumMode = "fixed";
    private int _scenarioIndex;
    private string _curriculumModeOverride = "";

    private readonly List<EpisodeDto> _episodes = new(512);
    private const int EpisodeCapacity = 512;
    private const int DiagnosticTicks = 200;
    private const string MlCoreMissingReason = "ML.Core not linked: set ML_CORE_PATH to ML.Core.csproj";

    public LifeLoop(
        WorldSim world,
        HomeostasisEngine homeostasis,
        InstinctEngine instincts,
        EmotionEngine emotion,
        ActionSelector selector,
        Actuator actuator,
        RewardEngine reward,
        LearningEngine learning,
        BrainConfigLoader configLoader,
        Action<LifeStateDto, CancellationToken> broadcast,
        Action<TraceDto, CancellationToken> trace,
        Action<LifeOutputDto, CancellationToken> output,
        Action<string> log)
    {
        _world = world;
        _homeostasis = homeostasis;
        _instincts = instincts;
        _emotion = emotion;
        _selector = selector;
        _actuator = actuator;
        _reward = reward;
        _rewardCalc = new RewardCalculator(reward);
        _learning = learning;
        _configLoader = configLoader;
        _broadcast = broadcast;
        _trace = trace;
        _output = output;
        _log = log;
        var (cfg, _) = _configLoader.GetCurrent();
        _longTermMemory = new PersistentEpisodicMemory(_log);
        _longTermMemory.Configure(cfg.Memory ?? MemoryConfig.Default);
        _episode = new EpisodeManager(cfg.Episode.MaxSteps);
        _curriculum = new CurriculumManager(cfg.Ml.Seed);
        _policyEvaluator = new PolicyEvaluator(_scenarioScorer);
        _ml = MlPolicyAdvisorFactory.Create(MlCoreAvailability.IsAvailable, cfg.Ml, _log);
    }

    public void Start() => _running = true;
    public void Stop() => _running = false;
    public void Step() => _stepRequested = true;
    public void ResetMl()
    {
        var (cfg, _) = _configLoader.GetCurrent();
        var mlCfg = cfg.Ml with { Enable = cfg.Ml.Enable || cfg.UseMlAdvisor };
        _ml.Reset(mlCfg);
        _ml.ResetCounters();
        _mlLoaded = true;
        TryDeleteCheckpoint(mlCfg.CheckpointPath);
        _log("ML сброшен");
    }

    public void RequestEpisodeReset()
    {
        _episode.RequestManualReset();
    }

    public IReadOnlyList<string> ListScenarios() => _curriculum.ListScenarios();

    public bool TrySetScenario(string name) => _curriculum.TrySetScenario(name);

    public void SetCurriculumMode(string mode)
    {
        _curriculumModeOverride = mode?.Trim().ToLowerInvariant() ?? "";
        _curriculum.SetMode(_curriculumModeOverride);
    }

    public void NextScenario()
    {
        _curriculum.Advance(double.PositiveInfinity);
    }

    public void SetMlMode(string mode)
    {
        _mlModeOverride = mode?.Trim().ToLowerInvariant() ?? "";
    }

    public string GetMlStatus()
    {
        var (cfg, _) = _configLoader.GetCurrent();
        var mlCfg = cfg.Ml with { Enable = cfg.Ml.Enable || cfg.UseMlAdvisor };
        var backendKind = (mlCfg.Backend ?? "off").Trim().ToLowerInvariant();
        var (enabled, reason) = ComputeMlEnabled(mlCfg, backendKind, mlCfg.RemoteStrict, _ml.TryConnectRemote());
        var mlMode = ResolveMlMode(mlCfg);
        var trainEnabled = mlMode != "evaluation" && enabled;
        var t = _ml.BuildTelemetry(enabled, MlCoreAvailability.IsAvailable, StateVectorizer.InputDim, ActionCatalog.Count, _avgReward200, reason, mlMode, trainEnabled, _trainingEpisodeCount, _evalEpisodeCount);
        var lastError = string.IsNullOrWhiteSpace(t.LastRemoteError)
            ? "нет"
            : RussianDisplay.Token(t.LastRemoteError);
        var disabledReason = enabled
            ? "не применимо"
            : RussianDisplay.Token(reason);
        return $"включён={RussianDisplay.YesNo(enabled)} " +
               $"backend={RussianDisplay.Token(t.BackendKind)} " +
               $"локальное ядро доступно={RussianDisplay.YesNo(t.CoreAvailable)} " +
               $"удалённое соединение={RussianDisplay.YesNo(t.RemoteConnected)} " +
               $"RTT={t.RttMs:0} мс " +
               $"режим={RussianDisplay.Token(t.MlMode)} " +
               $"обучение={RussianDisplay.YesNo(t.TrainEnabled)} " +
               $"источник={RussianDisplay.Token(t.PolicySource)} " +
               $"буфер={t.BufferSize}/{t.BufferCapacity} " +
               $"шаги={t.TrainSteps} " +
               $"ε={t.Epsilon:0.000} " +
               $"вес сети={t.NetWeight:0.00} " +
               $"loss={t.LastLoss:0.000} " +
               $"последняя ошибка={lastError} " +
               $"причина отключения={disabledReason}";
    }

    public bool TryConnectMl() => _ml.TryConnectRemote();
    public void DisconnectMl() => _ml.DisconnectRemote();

    public string GetMemoryStatus()
    {
        var (config, _) = _configLoader.GetCurrent();
        _longTermMemory.Configure(config.Memory ?? MemoryConfig.Default);
        var status = _longTermMemory.GetStatus();
        var lastStored = status.LastStoredAt?.ToString("O") ?? "нет";
        var lastError = string.IsNullOrWhiteSpace(status.LastError) ? "нет" : status.LastError;
        return $"включена={RussianDisplay.YesNo(status.Enabled)} " +
               $"записей={status.EpisodeCount}/{status.Capacity} " +
               $"эпизодов={status.RepresentedEpisodes} " +
               $"опыт сценариев={status.ScenarioExperiences} " +
               $"индекс={status.IndexedBuckets} кандидатов={status.LastCandidateCount} " +
               $"запросов воспоминаний={status.RecallRequests} " +
               $"последний поиск={status.LastRecallCount} " +
               $"сходство={status.LastBestSimilarity:0.000} " +
               $"объединено={status.DuplicateMerges} забыто={status.ForgottenEntries} " +
               $"консолидаций={status.ConsolidationRuns} " +
               $"последняя запись={lastStored} " +
               $"повреждённых строк={status.InvalidLines} " +
               $"ошибка={lastError} " +
               $"путь={status.Path}";
    }

    public IReadOnlyList<string> GetRecentMemoryLines(int count)
    {
        var (config, _) = _configLoader.GetCurrent();
        _longTermMemory.Configure(config.Memory ?? MemoryConfig.Default);
        return _longTermMemory.GetRecent(count).Select(FormatMemoryEntry).ToList();
    }

    public IReadOnlyList<string> SearchMemoryLines(string query, int count)
    {
        var (config, _) = _configLoader.GetCurrent();
        _longTermMemory.Configure(config.Memory ?? MemoryConfig.Default);
        return _longTermMemory.Search(query, count).Select(FormatMemoryEntry).ToList();
    }

    public IReadOnlyList<string> GetMemoryStatsLines()
    {
        var (config, _) = _configLoader.GetCurrent();
        _longTermMemory.Configure(config.Memory ?? MemoryConfig.Default);
        var stats = _longTermMemory.GetStatistics();
        var lines = new List<string>
        {
            $"записей={stats.EntryCount} представлено эпизодов={stats.RepresentedEpisodes} " +
            $"сценариев={stats.ScenarioCount} устойчивых={stats.MatureScenarioCount} " +
            $"индекс={stats.IndexBuckets} кандидатов последнего поиска={stats.LastCandidateCount}",
            $"объединено={stats.DuplicateMerges} забыто={stats.ForgottenEntries} " +
            $"консолидаций={stats.ConsolidationRuns} средняя значимость={stats.AverageSalience:0.000} " +
            $"средняя сила={stats.AverageStrength:0.000} опыт={stats.ExperiencePath}"
        };

        foreach (var experience in _longTermMemory.GetScenarioExperiences())
        {
            lines.Add($"сценарий={RussianDisplay.Token(experience.ScenarioName)} " +
                      $"эпизодов={experience.EpisodeCount} успех={experience.SuccessRate:P0} " +
                      $"награда={experience.AvgReward:0.000} уверенность={experience.Confidence:0.000} " +
                      $"помогло={RussianDisplay.Token(experience.HelpfulAction ?? "нет")} " +
                      $"мешало={RussianDisplay.Token(experience.HarmfulAction ?? "нет")}");
        }

        return lines;
    }

    public IReadOnlyList<string> ExplainMemoryLines(string? scenario)
    {
        var (config, _) = _configLoader.GetCurrent();
        _longTermMemory.Configure(config.Memory ?? MemoryConfig.Default);
        var cue = new MemoryCue(
            string.IsNullOrWhiteSpace(scenario) ? _scenarioName : scenario.Trim(),
            _affect.Mood,
            _homeo.Pain,
            _homeo.Safety,
            _homeo.Arousal);
        var explanation = _longTermMemory.Explain(cue);
        var lines = new List<string>
        {
            $"сценарий={RussianDisplay.Token(cue.ScenarioName)} настроение={RussianDisplay.Token(cue.Mood)} " +
            $"кандидатов={explanation.IndexedCandidates} воспоминаний={explanation.RecalledEpisodes} " +
            $"сходство={explanation.BestSimilarity:0.000} уверенность={explanation.Confidence:0.000}",
            $"что помогло={RussianDisplay.Token(explanation.HelpfulAction ?? "нет")} " +
            $"влияние={explanation.HelpfulScore:+0.000;-0.000;0.000} " +
            $"что мешало={RussianDisplay.Token(explanation.HarmfulAction ?? "нет")} " +
            $"влияние={explanation.HarmfulScore:+0.000;-0.000;0.000}"
        };

        if (explanation.ScenarioExperience is { } experience)
        {
            lines.Add($"устойчивый опыт: эпизодов={experience.EpisodeCount} успех={experience.SuccessRate:P0} " +
                      $"средняя награда={experience.AvgReward:0.000} зрелый={RussianDisplay.YesNo(experience.IsMature)}");
        }

        lines.AddRange(explanation.Evidence.Select(item =>
            $"свидетельство эпизод={item.EpisodeId} повторов={item.Occurrences} " +
            $"сходство={item.Similarity:0.000} релевантность={item.Relevance:0.000} " +
            $"успех={RussianDisplay.YesNo(item.ScenarioPassed)} награда={item.AvgReward:0.000} " +
            $"помогло={RussianDisplay.Token(item.HelpfulAction ?? "нет")}"));
        return lines;
    }

    public IReadOnlyList<string> GetCortexMemoryLines()
    {
        var (config, _) = _configLoader.GetCurrent();
        _longTermMemory.Configure(config.Memory ?? MemoryConfig.Default);
        var cue = new MemoryCue(
            _scenarioName,
            _affect.Mood,
            _homeo.Pain,
            _homeo.Safety,
            _homeo.Arousal);
        var explanation = _longTermMemory.Explain(cue);
        var lines = new List<string>
        {
            $"recall={explanation.RecalledEpisodes};sim={FormatCortexNumber(explanation.BestSimilarity)};" +
            $"help={CompactCortexToken(explanation.HelpfulAction)};" +
            $"helpScore={FormatCortexNumber(explanation.HelpfulScore)};" +
            $"harm={CompactCortexToken(explanation.HarmfulAction)};" +
            $"harmScore={FormatCortexNumber(explanation.HarmfulScore)};" +
            $"conf={FormatCortexNumber(explanation.Confidence)}"
        };

        if (explanation.ScenarioExperience is { } experience)
        {
            lines.Add(
                $"episodes={experience.EpisodeCount};" +
                $"success={FormatCortexNumber(experience.SuccessRate)};" +
                $"avg={FormatCortexNumber(experience.AvgReward)};" +
                $"mature={(experience.IsMature ? 1 : 0)}");
        }

        return lines;
    }

    public string ConsolidateMemory()
    {
        var (config, _) = _configLoader.GetCurrent();
        _longTermMemory.Configure(config.Memory ?? MemoryConfig.Default);
        var result = _longTermMemory.Consolidate();
        return $"записей={result.EntriesBefore}→{result.EntriesAfter} " +
               $"представлено эпизодов={result.RepresentedEpisodes} объединено={result.MergedEntries} " +
               $"забыто={result.ForgottenEntries} опыт сценариев={result.ScenarioExperiences} " +
               $"время={result.DurationMs} мс";
    }

    private static (bool Enabled, string? Reason) ComputeMlEnabled(MlConfig cfg, string backendKind, bool remoteStrict, bool remoteConnected)
    {
        if (!cfg.Enable)
            return (false, "ml.enable=false (brainconfig)");

        if (backendKind == "off")
            return (false, "backend=off");

        if (backendKind == "local" && !MlCoreAvailability.IsAvailable)
            return (false, MlCoreMissingReason);

        if (backendKind == "remote" && !remoteConnected)
        {
            var reason = "remote not connected: run mlconnect or check host/port";
            return remoteStrict ? (false, reason) : (true, null);
        }

        return (true, null);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var (config, _) = _configLoader.GetCurrent();
            var tickRate = Math.Max(1.0, config.TickRate);
            var dtSeconds = 1.0 / tickRate;
            var delayMs = Math.Max(1, (int)Math.Round(1000.0 / tickRate));

            if (_running || _stepRequested)
            {
                TickOnce(dtSeconds, ct);
                _stepRequested = false;
                if (_running)
                    await Task.Delay(delayMs, ct);
            }
            else
            {
                await Task.Delay(50, ct);
            }
        }
    }

    private void TickOnce(double dtSeconds, CancellationToken ct)
    {
        var beforeHomeo = _homeo;
        var beforeAffect = _affect;
        var (config, version) = _configLoader.GetCurrent();
        _configVersion = version;
        var curriculumCfg = config.Curriculum ?? BrainConfig.Default.Curriculum;
        var evalCfg = config.Evaluation ?? BrainConfig.Default.Evaluation;
        var memoryCfg = config.Memory ?? MemoryConfig.Default;
        _longTermMemory.Configure(memoryCfg);
        var scenarioCfg = config.Scenarios ?? BrainConfig.Default.Scenarios;
        var rewardWeights = config.RewardWeights ?? BrainConfig.Default.RewardWeights ?? new RewardWeightsConfig(1.0, 1.0, 1.0, 0.05, 0.05);
        if (!string.IsNullOrWhiteSpace(_curriculumModeOverride))
            curriculumCfg = curriculumCfg with { Mode = _curriculumModeOverride };
        else if (!string.IsNullOrWhiteSpace(config.CurriculumMode))
            curriculumCfg = curriculumCfg with { Mode = config.CurriculumMode };
        _curriculum.Configure(curriculumCfg, scenarioCfg);
        if (_tick == 0 && !string.IsNullOrWhiteSpace(config.ScenarioDefault))
            _curriculum.TrySetScenario(config.ScenarioDefault);
        _policyEvaluator.Configure(evalCfg);
        var scenario = _curriculum.GetScenario();
        _scenarioName = scenario.Name;
        _curriculumMode = _curriculum.Mode;
        _scenarioIndex = _curriculum.ScenarioIndex;
        var worldConfig = scenario.Apply(config.World);

        _selfTalkThrottle.Configure(config.SelfTalkCooldown, config.SelfTalkRepeatCooldownTicks, config.SelfTalkSemanticCooldownTicks);
        var rewardConfig = config.Reward with
        {
            HomeostasisWeight = rewardWeights.Homeostasis,
            ExploreWeight = rewardWeights.Explore,
            SocialWeight = rewardWeights.Social,
            LoopPenaltyWeight = rewardWeights.LoopPenalty,
            InvalidActionPenalty = rewardWeights.InvalidActionPenalty
        };
        var episodeConfig = config.Episode with
        {
            LoopStrengthThreshold = config.LoopThreshold
        };
        var mlConfig = config.Ml with
        {
            Enable = config.Ml.Enable || config.UseMlAdvisor,
            EpsilonStart = config.Epsilon,
            EpsilonEnd = config.EpsilonMin,
            EpsilonMin = config.EpsilonMin,
            EpsilonDecay = config.EpsilonDecay
        };
        var mlMode = ResolveMlMode(mlConfig);
        var trainEnabled = mlMode != "evaluation";
        var mlConfigEffective = trainEnabled
            ? mlConfig
            : mlConfig with { EpsilonStart = 0, EpsilonEnd = 0, EpsilonMin = 0, EpsilonDecay = 0, TrainEveryTicks = int.MaxValue, TrainStepsPerBatch = 0 };

        _episode.Configure(episodeConfig, scenario.EpisodeLengthMultiplier);
        _loop.Configure(mlConfig.LoopWindow, mlConfig.LoopSameK, mlConfig.LoopAltK);
        var backendKind = (mlConfig.Backend ?? "off").Trim().ToLowerInvariant();
        var remoteConnected = backendKind == "remote" && _ml.TryConnectRemote();
        var (mlEnabled, disableReason) = ComputeMlEnabled(mlConfig, backendKind, mlConfig.RemoteStrict, remoteConnected);
        if (!mlEnabled && disableReason?.Contains("ML.Core not linked") == true && !_mlCoreMissingLogged)
        {
            _log("Предупреждение: ML.Core не подключён; ml.enable принудительно отключён");
            _mlCoreMissingLogged = true;
        }
        mlConfig = mlConfig with { Enable = mlEnabled };
        trainEnabled = trainEnabled && mlEnabled;
        mlConfigEffective = trainEnabled
            ? mlConfig
            : mlConfig with { EpsilonStart = 0, EpsilonEnd = 0, EpsilonMin = 0, EpsilonDecay = 0, TrainEveryTicks = int.MaxValue, TrainStepsPerBatch = 0 };
        if (mlConfig.Enable && !_mlLoaded)
        {
            if (_ml.TryLoad(mlConfig.CheckpointPath, out var loadedEpisodeId))
            {
                _episode.SetEpisodeId(loadedEpisodeId);
                _log($"Политика ML загружена: {mlConfig.CheckpointPath}");
            }
            _mlLoaded = true;
        }
        if (mlConfig.Enable && _tick > 0 && _tick % 500 == 0)
            _ml.TrySave(mlConfig.CheckpointPath, _episode.EpisodeId);

        _clock.Tick(dtSeconds, _sleep.IsSleeping);
        _sleep.Update(_clock, ref _homeo, ref _affect);
        if (_sleep.EnteredSleep)
        {
            _sleepConsolidated = false;
            _log("ПЕРЕХОД В СОН");
        }
        if (_sleep.WokeUp)
            _log("ПРОБУЖДЕНИЕ");

        if (_sleep.IsSleeping)
        {
            if (!_sleepConsolidated)
            {
                var sums = _memory.Consolidate();
                foreach (var kv in sums)
                {
                    if (kv.Value > 0)
                        _learning.AddBias(kv.Key, 0.02);
                    else if (kv.Value < 0)
                        _learning.AddBias(kv.Key, -0.01);
                }
                _semantic.Consolidate();
                _loop.ResetShortTerm();
                _sleepConsolidated = true;
            }

            var circadian = _clock.Snapshot(true);
            var sleepScenarioInfo = new DeepBrain.Shared.BrainDtos.V6.ScenarioInfoDto(
                _scenarioName,
                _curriculumMode,
                _scenarioIndex
            );
            var sleepEvalSnapshot = _policyEvaluator.Snapshot(mlMode == "evaluation");
            var sleepEvalDto = ToEvaluationDto(sleepEvalSnapshot);
            var stateSleep = new LifeStateDto(
                _tick,
                DateTimeOffset.Now,
                _homeo,
                _instincts.Compute(_homeo, _world, config.Drives),
                _affect,
                "sleep",
                0,
                null,
                "",
                1.0 - _affect.Arousal,
                circadian,
                Array.Empty<DeepBrain.Shared.BrainDtos.V3.GoalDto>(),
                null,
                null,
                _world.Events.GetRecent(5),
                _semantic.GetTopNotes(3),
                new DeepBrain.Shared.BrainDtos.V5.CharacterStateDto(
                    _personality.Persona,
                    _habits.GetTopHabits(5),
                    null,
                    0.0,
                    "calm",
                    0,
                    _cooldowns.GetLastTick("emit_message"),
                    _world.Events.ConsumedCount
                ),
                new DeepBrain.Shared.BrainDtos.V5.ClimateDto(_world.CalmLevel, _world.StressLevel, _world.Tension, _world.BaselineTension),
                new DeepBrain.Shared.BrainDtos.V5.PainSourceDto(0, 0, 0, 0),
                _configVersion,
                null,
                _statsSnapshot,
                _ml.BuildTelemetry(mlConfig.Enable, MlCoreAvailability.IsAvailable, StateVectorizer.InputDim, ActionCatalog.Count, _avgReward200, disableReason, mlMode, trainEnabled, _trainingEpisodeCount, _evalEpisodeCount),
                null,
                new DeepBrain.Shared.BrainDtos.V6.EpisodeInfoDto(
                    _episode.EpisodeId,
                    _episode.EpisodeTick,
                    _episode.EpisodeLengthTicks,
                    "none"
                ),
                sleepScenarioInfo,
                sleepEvalDto,
                new DecisionDto("sleep", "internal", "sleep", "ok", "heuristic"),
                new ScenarioDto(_scenarioName),
                new CurriculumStateDto(_curriculumMode, _scenarioIndex),
                new TickInfoDto(_tick, _episode.EpisodeTick, dtSeconds),
                new LoopInfoDto(_loop.IsInLoop, _loop.LoopObservedCount, _loop.LoopObservedCount, _loop.LoopStrength, 0.0, NormalizeLoopType(_loop.LoopType), _loop.Streak)
            );
            EmitTrace("tick", $"тик={_tick} эпизод={_episode.EpisodeId} действие=сон награда=0.000 петля=нет ε=0.000", ct);
            _broadcast(stateSleep, ct);
            _tick++;
            return;
        }

        var circ = _clock.Snapshot(false);
        var preInstincts = _instincts.Compute(_homeo, _world, config.Drives);
        var newEvents = _world.Tick(_tick, circ.Phase, circ.SleepPressure, _sleep.IsSleeping, dtSeconds, preInstincts.Attachment, worldConfig);
        foreach (var ev in newEvents)
            _log($"СОБЫТИЕ: {RussianDisplay.Token(ev.Type)} {ev.Severity:0.00} {RussianDisplay.Token(ev.Payload)}");
        foreach (var ev in newEvents)
            _eventCounts[ev.Type] = _eventCounts.TryGetValue(ev.Type, out var count) ? count + 1 : 1;
        var recentEvents = _world.Events.GetRecent(5);
        var hasPendingSocialSignal = SocialContactResponder.HasPending(_world.Events);
        if (_tick % 50 == 0)
            EmitTrace("world.climate", new { calm = _world.CalmLevel, stress = _world.StressLevel, tension = _world.Tension, baseline = _world.BaselineTension, lastMajor = _world.LastMajorEvent }, ct);

        _homeo = _homeostasis.Update(_homeo, _world, dtSeconds);
        var painSource = ApplyPainSafetyAdjustments(ref _homeo, circ, recentEvents, newEvents, dtSeconds, config.Pain);
        var instincts = _instincts.Compute(_homeo, _world, config.Drives);
        instincts = instincts with
        {
            Agency = LifeMath.Clamp01(instincts.Agency - _agencyOffset)
        };

        var appraisal = _appraisal.Compute(_homeo, instincts, _world, circ, recentEvents);
        if (_loopBreakTicks > 0)
        {
            appraisal = appraisal with
            {
                Threat = LifeMath.Clamp01(appraisal.Threat - 0.2),
                Novelty = LifeMath.Clamp01(appraisal.Novelty + 0.2)
            };
            _loopBreakTicks--;
        }
        var computedAttention = _attention.Compute(_homeo, instincts, _affect, circ, recentEvents, _world.Tension, dtSeconds);
        var attention = _attentionInertia.Apply(computedAttention);
        if (_loopBreakTicks > 0 && attention.Focus1 == "threat")
            attention = attention with { Focus1 = "novelty", Reason = "loop_break" };
        var computedAffect = _emotion.Compute(instincts, _homeo, _world.CalmLevel, _world.StressLevel, attention, recentEvents, config.Mood, appraisal);
        var (inertAffect, moodInertia) = _inertia.Apply(_affect, computedAffect, instincts.SelfPreservation);
        _affect = inertAffect;
        _personality.ApplyBaselines(ref _affect, ref instincts);
        var topEventSalience = recentEvents.FirstOrDefault()?.Salience ?? 0;
        _calmBaseline.Apply(ref _affect, ref instincts, dtSeconds, _world.StressLevel, topEventSalience);

        var dominantDrive = _driveResolver.Resolve(instincts);
        var goals = _goals.Resolve(_homeo, instincts, dominantDrive, circ.Phase);
        if (_world.CalmLevel > 0.6 && _homeo.Energy > 0.5 && _homeo.Pain < 0.4)
            goals = ApplyExploreRebound(goals);
        var activePlan = _plan.Update(goals, dominantDrive, _loop.LoopPenalty, _sleep.IsSleeping, instincts.SelfPreservation);
        if (_plan.Created)
            _log($"ПЛАН СОЗДАН: {RussianDisplay.Token(activePlan?.Strategy)} " +
                 $"цель={RussianDisplay.Token(activePlan?.GoalId)} " +
                 $"осталось тиков={activePlan?.RemainingTicks}");
        if (_plan.Interrupted)
            _log("ПЛАН ПРЕРВАН");

        goals = _personality.BiasGoals(goals, circ.Phase, recentEvents);
        var semanticKey = $"{_affect.Mood}+drive={dominantDrive}+phase={circ.Phase}+focus={attention.Focus1}";
        var cueKey = _habits.ComputeCue(attention, circ, _loop, recentEvents, _homeo);
        var (habitAction, habitStrength, habitId) = _habits.Suggest(cueKey);
        if (habitId is not null)
            habitStrength *= _habits.GetSatiationFactor(habitId, _tick);
        var habitInfluence = _habits.ComputeInfluence(_personality.Persona, habitStrength);
        if (habitAction is null)
            habitInfluence = 0.0;
        habitInfluence = Math.Min(0.4, habitInfluence);
        if (_loop.LoopPenalty > 0.4)
            habitInfluence *= 0.7;
        var voiceMode = _voice.Resolve(_personality.Persona, _affect, instincts, circ);
        var isAnxious = _affect.Mood == "anxious" || instincts.SelfPreservation > 0.8;
        var emitCooldown = ComputeEmitCooldownTicks(voiceMode, _personality.Persona.AttachmentBaseline, isAnxious, config.Actions);
        var allowVariety = _world.Threat < 0.35 && attention.Focus1 != "threat";
        var calmExploreBoost = _world.CalmLevel > 0.6 && _homeo.Energy > 0.5 && _homeo.Pain < 0.4;

        var (candidates, strategy) = _selector.BuildCandidates(
            _homeo,
            instincts,
            _affect,
            _learning,
            _loop,
            _memory,
            _cooldowns,
            _tick,
            habitAction,
            habitStrength,
            habitInfluence,
            emitCooldown,
            allowVariety,
            calmExploreBoost,
            dominantDrive,
            activePlan?.Strategy,
            attention.Focus1,
            _semantic,
            semanticKey,
            appraisal,
            config.Actions,
            hasPendingSocialSignal
        );

        var memoryRecall = _longTermMemory.BuildActionBias(new MemoryCue(
            _scenarioName,
            _affect.Mood,
            _homeo.Pain,
            _homeo.Safety,
            _homeo.Arousal
        ));
        ApplyLongTermMemoryBias(candidates, memoryRecall);
        if (_tick % DiagnosticTicks == 0 && memoryRecall.RecallCount > 0)
        {
            var strongest = memoryRecall.ActionBiases
                .OrderByDescending(pair => Math.Abs(pair.Value))
                .FirstOrDefault();
            EmitTrace("memory.recall", new
            {
                recalled = memoryRecall.RecallCount,
                candidates = memoryRecall.IndexedCandidateCount,
                similarity = memoryRecall.BestSimilarity,
                episodeId = memoryRecall.BestEpisodeId,
                actionName = strongest.Key,
                bias = strongest.Value,
                helpfulAction = memoryRecall.HelpfulAction,
                harmfulAction = memoryRecall.HarmfulAction,
                confidence = memoryRecall.Confidence
            }, ct);
        }

        var actionMask = _masker.BuildMask(
            _homeo,
            _affect,
            _cooldowns,
            _tick,
            emitCooldown,
            config.Actions,
            _loop.IsLoopDetected,
            episodeConfig.PanicSafetyMin);
        candidates = candidates
            .Where(c => IsMaskAllowed(actionMask, c.Action.Name))
            .ToList();

        var maskFallback = false;
        if (candidates.Count == 0)
        {
            var recoveryActions = ActionCatalog.Actions
                .Where(action => IsMaskAllowed(actionMask, action))
                .ToList();
            candidates.AddRange(_selector.BuildRecoveryCandidates(recoveryActions, _learning, _cooldowns, _tick));
            maskFallback = true;
        }

        var allowedActions = candidates
            .Select(c => c.Action.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var decisionMask = BuildCandidateMask(actionMask, allowedActions);
        var heuristicScores = ActionCatalog.Actions.ToDictionary(a => a, _ => double.NegativeInfinity, StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            if (heuristicScores.TryGetValue(c.Action.Name, out var current))
                heuristicScores[c.Action.Name] = Math.Max(current, c.Score);
        }

        var stateVec = mlConfigEffective.Enable
            ? _ml.Encode(new StateVectorInput(
                _homeo,
                instincts,
                _affect,
                moodInertia,
                circ,
                new ClimateDto(_world.CalmLevel, _world.StressLevel, _world.Tension, _world.BaselineTension),
                attention,
                _loop.LoopPenalty,
                _loop.AvgRewardShort,
                _personality.Persona,
                habitInfluence,
                recentEvents.FirstOrDefault()?.Salience ?? 0,
                appraisal
            ))
            : Array.Empty<float>();

        var decision = _ml.SelectAction(stateVec, heuristicScores, allowedActions, decisionMask, mlConfigEffective, _tick);
        var chosen = candidates.FirstOrDefault(c => c.Action.Name == decision.ActionName) ?? ActionSelector.PickBest(candidates);
        var action = chosen.Action;
        var maskStatus = maskFallback ? "fallback" : "ok";
        var reason = decision.PolicySource == "heuristic" ? chosen.Reason : $"{chosen.Reason}|{decision.PolicySource}";
        reason = NormalizeDecisionReason(reason, maskFallback);
        if (decision.IllegalChoice)
            _log("ML выбрал недопустимое действие; применён резервный выбор");

        var invalidAction = decision.IllegalChoice || !IsMaskAllowed(actionMask, action.Name);
        EmitTrace("decision", new { actionName = action.Name, kind = action.Kind, strength = action.Strength, reason, mask = maskStatus }, ct);

        var outcome = _actuator.Apply(action, ref _homeo, ref _affect);

        if (action.Name == "focus_narrow")
            _agencyOffset = LifeMath.Clamp01(_agencyOffset + 0.1 * action.Strength);
        else
            _agencyOffset = LifeMath.Clamp01(_agencyOffset - 0.02);

        if (action.Name == "explore_signal")
            _world.DampenNovelty(action.Strength);

        var socialSignalsHandled = action.Name == "emit_message"
            ? SocialContactResponder.ConsumePending(_world.Events)
            : 0;
        var socialContactHandled = socialSignalsHandled > 0;
        if (socialContactHandled)
            EmitTrace("social.response", new { consumed = socialSignalsHandled, tick = _tick }, ct);

        var rewardBase = _rewardCalc.Compute(beforeHomeo, _homeo, action.Name, appraisal, false, 0.0, invalidAction, socialContactHandled, rewardConfig);
        var regBonus = ComputeRegulationBonus(action, beforeHomeo, _homeo, beforeAffect, _affect);
        rewardBase = ApplyHomeostasisBonus(rewardBase, regBonus);
        _loop.Update(action.Name, _affect.Mood, _affect.Arousal, _homeo.Energy, _homeo.Fatigue, dominantDrive, circ.Phase, attention.Focus1, rewardBase.Total, _tick);
        var rewardDto = _rewardCalc.Compute(beforeHomeo, _homeo, action.Name, appraisal, _loop.IsInLoop, _loop.LoopStrength, invalidAction, socialContactHandled, rewardConfig);
        rewardDto = ApplyHomeostasisBonus(rewardDto, regBonus);

        var isPanic = _episode.IsPanic(_homeo.Safety, _homeo.Pain, _world.Threat);
        var pendingEpisodeReset = _episode.Tick(_loop.LoopStrength, _loop.IsLoopDetected, isPanic, out var episodeResetReason);
        var resetReason = pendingEpisodeReset && !string.IsNullOrWhiteSpace(episodeResetReason)
            ? episodeResetReason!
            : "none";

        if (resetReason == "panic")
            rewardDto = PanicRecovery.ApplyTerminalPenalty(rewardDto, episodeConfig);

        _learning.Update(action.Name, rewardDto.Total);
        _cooldowns.Mark(action.Name, _tick);
        UpdateStats(action.Name, _affect.Mood, _homeo, attention, rewardDto.Total);
        UpdateEpisodeMetrics(action, _affect.Mood, rewardDto, _loop.IsLoopDetected, _loop.LoopStrength, _homeo, socialContactHandled, maskFallback, invalidAction, decision.UsedMl, decision.Epsilon);

        if (_loop.IsLoopDetected && _loop.ShouldAnnounceLoop(_tick))
        {
            EmitTrace("loop.detected", new { type = _loop.LoopType, streak = _loop.Streak, strength = _loop.LoopStrength, avgR = _loop.AvgRewardShort }, ct);
            _log($"ОБНАРУЖЕНА ПЕТЛЯ тип={RussianDisplay.Token(_loop.LoopType)} " +
                 $"тик={_tick} сила={_loop.LoopStrength:0.00}");
        }

        if (trainEnabled && stateVec.Length > 0)
        {
            var nextInstincts = _instincts.Compute(_homeo, _world, config.Drives);
            var nextRecentEvents = _world.Events.GetRecent(5);
            var nextAppraisal = _appraisal.Compute(_homeo, nextInstincts, _world, circ, nextRecentEvents);
            var nextVec = _ml.Encode(new StateVectorInput(
                _homeo,
                nextInstincts,
                _affect,
                moodInertia,
                circ,
                new ClimateDto(_world.CalmLevel, _world.StressLevel, _world.Tension, _world.BaselineTension),
                attention,
                _loop.LoopPenalty,
                _loop.AvgRewardShort,
                _personality.Persona,
                habitInfluence,
                nextRecentEvents.FirstOrDefault()?.Salience ?? 0,
                nextAppraisal
            ));
            var actionIdx = ActionCatalog.IndexOf(action.Name);
            var nextMask = _masker.BuildMask(
                _homeo,
                _affect,
                _cooldowns,
                _tick + 1,
                emitCooldown,
                config.Actions,
                _loop.IsLoopDetected,
                episodeConfig.PanicSafetyMin);
            var done = resetReason != "none";
            _ml.Observe(stateVec, actionIdx, (float)rewardDto.Total, nextVec, done, nextMask, mlConfigEffective, _tick);
        }

        if (action.Name == "loop_break")
        {
            _loopBreakTicks = Math.Max(_loopBreakTicks, mlConfig.LoopBreakTicks);
            _cooldowns.ResetExcept("emit_message", "loop_break");
        }

        outcome = new OutcomeDto(outcome.Action, rewardDto.Total, outcome.Message);

        EmitTrace("homeostasis", _homeo, ct);
        EmitTrace("instincts", instincts, ct);
        EmitTrace("affect", _affect, ct);
        EmitTrace("action", new
        {
            actionName = action.Name,
            kind = action.Kind,
            deltaSummary = new
            {
                Energy = _homeo.Energy - beforeHomeo.Energy,
                Fatigue = _homeo.Fatigue - beforeHomeo.Fatigue,
                Safety = _homeo.Safety - beforeHomeo.Safety,
                Arousal = _homeo.Arousal - beforeHomeo.Arousal,
                Valence = _affect.Valence - beforeAffect.Valence
            }
        }, ct);
        var rewardLine = FormatRewardTraceLine(rewardDto);
        EmitTrace("reward", rewardLine, ct);

        if (action.Kind == "external" && string.IsNullOrWhiteSpace(outcome.Message))
            outcome = new OutcomeDto(outcome.Action, outcome.Reward, $"action={action.Name}");

        if (!string.IsNullOrWhiteSpace(outcome.Message))
        {
            var msg = FormatOutputMessage(outcome.Message!.Trim(), action.Name, voiceMode);
            if (_dedupe.ShouldPublish(_tick, msg))
            {
                _log($"[жизнь] ВЫВОД: {msg}");
                EmitTrace("output", new { message = msg, actionName = action.Name }, ct);
                _outputSinceDiag = msg;
                _output(new LifeOutputDto(_tick, DateTimeOffset.Now, msg, action.Name), ct);
            }
        }

        if (action.Name == "emit_message")
        {
            if (socialContactHandled)
                _log($"ОБРАБОТАНО СОБЫТИЕ сигнал контакта, тик={_tick}");
        }
        else if (action.Name == "explore_signal")
        {
            var count = _world.Events.Consume(e => (e.Type == "calm_window" || e.Type == "novelty_opportunity") && e.Salience > 0.3);
            if (count > 0)
                _log($"ОБРАБОТАНО СОБЫТИЕ окно исследования, тик={_tick}");
        }
        else if (action.Name == "breathe_slow" && attention.Focus1 == "threat")
        {
            var count = _world.Events.Consume(e => e.Type == "threat_spike" && e.Salience > 0.3);
            if (count > 0)
                _log($"ОБРАБОТАНО СОБЫТИЕ всплеск угрозы, тик={_tick}");
        }

        var policy = new DeepBrain.Shared.BrainDtos.V2.PolicyContextDto(
            strategy,
            $"мотив={RussianDisplay.Token(dominantDrive)} " +
            $"штраф петли={_loop.LoopPenalty:0.00} → " +
            $"{RussianDisplay.Token(strategy)}:{RussianDisplay.Token(action.Name)}",
            _loop.LoopCount,
            _loop.LoopPenalty,
            _loop.LastAction,
            _loop.SameActionStreak,
            _loop.AvgRewardShort
        );

        var mlTelemetry = _ml.BuildTelemetry(mlConfigEffective.Enable, MlCoreAvailability.IsAvailable, StateVectorizer.InputDim, ActionCatalog.Count, _avgReward200, disableReason, mlMode, trainEnabled, _trainingEpisodeCount, _evalEpisodeCount);
        _mlTelemetry = mlTelemetry;
        if (trainEnabled && _tick % 200 == 0)
            EmitTrace("ml.train", new { step = mlTelemetry.TrainSteps, loss = mlTelemetry.LastLoss }, ct);

        var emitRemaining = Math.Max(0, emitCooldown - (int)(_tick - _cooldowns.GetLastTick("emit_message")));
        var episodeInfo = new DeepBrain.Shared.BrainDtos.V6.EpisodeInfoDto(
            _episode.EpisodeId,
            _episode.EpisodeTick,
            _episode.EpisodeLengthTicks,
            resetReason
        );
        var scenarioInfo = new DeepBrain.Shared.BrainDtos.V6.ScenarioInfoDto(
            _scenarioName,
            _curriculumMode,
            _scenarioIndex
        );
        var decisionDto = new DecisionDto(
            action.Name,
            action.Kind,
            reason,
            maskStatus,
            decision.PolicySource
        );
        var loopInfo = new LoopInfoDto(
            _loop.IsInLoop,
            _loop.LoopObservedCount,
            _loop.LoopObservedCount,
            _loop.LoopStrength,
            Math.Abs(rewardDto.LoopPenalty),
            NormalizeLoopType(_loop.LoopType),
            _loop.Streak
        );
        var evalSnapshot = _policyEvaluator.Snapshot(mlMode == "evaluation");
        var evalDto = ToEvaluationDto(evalSnapshot);
        var state = new LifeStateDto(
            _tick,
            DateTimeOffset.Now,
            _homeo,
            instincts,
            _affect,
            action.Name,
            rewardDto.Total,
            policy,
            dominantDrive,
            moodInertia,
            circ,
            goals,
            activePlan,
            attention,
            recentEvents,
            _semantic.GetTopNotes(3),
            new DeepBrain.Shared.BrainDtos.V5.CharacterStateDto(
                _personality.Persona,
                _habits.GetTopHabits(5),
                habitId,
                habitInfluence,
                voiceMode,
                emitRemaining,
                _cooldowns.GetLastTick("emit_message"),
                _world.Events.ConsumedCount
            ),
            new DeepBrain.Shared.BrainDtos.V5.ClimateDto(_world.CalmLevel, _world.StressLevel, _world.Tension, _world.BaselineTension),
            painSource,
            _configVersion,
            appraisal,
            _statsSnapshot,
            mlTelemetry,
            rewardDto,
            episodeInfo,
            scenarioInfo,
            evalDto,
            decisionDto,
            new ScenarioDto(_scenarioName),
            new CurriculumStateDto(_curriculumMode, _scenarioIndex),
            new TickInfoDto(_tick, _episode.EpisodeTick, dtSeconds),
            loopInfo
        );
        EmitTrace("tick",
            $"тик={_tick} эпизод={_episode.EpisodeId} " +
            $"действие={RussianDisplay.Token(action.Name)} награда={rewardDto.Total:0.000} " +
            $"петля={RussianDisplay.Token(_loop.LoopType)} ε={decision.Epsilon:0.000}", ct);

        var episode = new EpisodeDto(
            _tick,
            beforeHomeo,
            _homeo,
            action,
            rewardDto.Total,
            state.Ts
        );

        AppendEpisode(episode);
        _memory.Add(episode, activePlan?.GoalId ?? "none", strategy);
        _semantic.Update(semanticKey, action.Name, rewardDto.Total);

        if (activePlan is not null)
            _goals.ApplyGoalSatisfaction(activePlan.GoalId, rewardDto.Total > 0 ? 0.05 : -0.02);

        _broadcast(state, ct);

        if (resetReason != "none")
        {
            var backend = mlTelemetry.BackendKind;
            var report = BuildEpisodeReport(resetReason, _scenarioName, backend);
            var remembered = _longTermMemory.Remember(report);
            if (remembered is not null)
            {
                _log($"ПАМЯТЬ: сохранён эпизод id={remembered.EpisodeId} " +
                     $"сценарий={RussianDisplay.Token(remembered.ScenarioName)} " +
                     $"награда={remembered.AvgReward:0.000} значимость={remembered.Salience:0.00} " +
                     $"сила={remembered.Strength:0.00} повторов={remembered.Occurrences}");
                EmitTrace("memory.store", new
                {
                    episodeId = remembered.EpisodeId,
                    scenario = remembered.ScenarioName,
                    avgReward = remembered.AvgReward,
                    salience = remembered.Salience,
                    strength = remembered.Strength,
                    occurrences = remembered.Occurrences
                }, ct);
            }
            if (evalCfg.SaveReports)
                _reportWriter.Write(report);
            _policyEvaluator.Add(report, mlMode == "evaluation");
            if (mlMode == "evaluation")
                _evalEpisodeCount++;
            else
                _trainingEpisodeCount++;
            var gateReward = _policyEvaluator.Snapshot(mlMode == "evaluation").AvgReward;
            if (_curriculum.Advance(gateReward))
                _log($"СЛЕДУЮЩИЙ СЦЕНАРИЙ название={RussianDisplay.Token(_curriculum.ScenarioName)} " +
                     $"индекс={_curriculum.ScenarioIndex} режим={RussianDisplay.Token(_curriculum.Mode)}");
            ResetEpisode(resetReason, episodeConfig);
            _log($"СБРОС ЭПИЗОДА причина={RussianDisplay.Token(resetReason)} id={_episode.EpisodeId}");
            EmitTrace("episode.reset",
                $"эпизод={_episode.EpisodeId} причина={RussianDisplay.Token(resetReason)}", ct);
        }

        foreach (var change in _habits.ApplyDecay(_tick))
            _log($"ОСЛАБЛЕНИЕ ПРИВЫЧКИ: {RussianDisplay.Token(change.before.Id)} " +
                 $"{change.before.Strength:0.00} → {change.after.Strength:0.00}");

        if (_tick < DiagnosticTicks && _tick % 50 == 0)
        {
            var goalsLine = string.Join(",", goals.Select(g => $"{g.Id}:{g.Urgency:0.00}"));
            var planLine = activePlan is null
                ? "нет"
                : $"{RussianDisplay.Token(activePlan.Strategy)}/{RussianDisplay.Token(activePlan.GoalId)}/{activePlan.RemainingTicks}";
            var topEvent = recentEvents.FirstOrDefault();
            var topEventText = topEvent is null
                ? "нет"
                : $"{RussianDisplay.Token(topEvent.Type)}/{topEvent.Salience:0.00}";
            var habitLine = habitAction is null
                ? "нет"
                : $"{RussianDisplay.Token(habitAction)}/{habitStrength:0.00}";
            var line = $"тик={_tick} | фаза={RussianDisplay.Token(circ.Phase)} | " +
                       $"фокус={RussianDisplay.Token(attention.Focus1)}({attention.Intensity:0.00}) | " +
                       $"голос={RussianDisplay.Token(voiceMode)} | сигнал={RussianDisplay.Token(cueKey)} | " +
                       $"привычка={habitLine} | план={planLine} | " +
                       $"действие={RussianDisplay.Token(action.Name)}({RussianDisplay.Token(action.Kind)}) | " +
                       $"награда={rewardDto.Total:0.000} | штраф петли={_loop.LoopPenalty:0.00} | " +
                       $"внутренняя речь={RussianDisplay.YesNo(!string.IsNullOrWhiteSpace(_lastSelfTalk))}";
            _log(line);
            if (!string.IsNullOrWhiteSpace(_outputSinceDiag))
            {
                _log($"ВЫВОД: {_outputSinceDiag}");
                _outputSinceDiag = null;
            }
            if (_loop.SameActionStreak > 10)
                _log($"ПРЕДУПРЕЖДЕНИЕ О ПЕТЛЕ: стратегия={RussianDisplay.Token(strategy)} " +
                     $"действие={RussianDisplay.Token(action.Name)}");
        }

        if (_loop.IsInLoop && _loop.LoopStrength >= config.SelfTalkLoopMinStrength)
            _loopHighTicks++;
        else
            _loopHighTicks = 0;

        if (!_loop.IsInLoop || _loop.LoopStrength <= config.SelfTalkRecoveryThreshold)
            _loopLowTicks++;
        else
            _loopLowTicks = 0;

        var calmWindowFresh = recentEvents.Any(e =>
            e.Type == "calm_window" &&
            e.Salience > 0.3 &&
            e.AgeSeconds <= Math.Max(1.0, dtSeconds * (config.SelfTalkCalmWindowHoldTicks + 1)));
        if (calmWindowFresh)
        {
            _calmWindowTicks++;
            _calmWindowInactiveTicks = 0;
        }
        else
        {
            _calmWindowTicks = 0;
            _calmWindowInactiveTicks++;
        }

        if (_calmWindowInactiveTicks >= config.SelfTalkCalmWindowHoldTicks)
            _calmWindowOpen = false;

        var enteredLoopTransition = !_selfTalkLoopActive && _loopHighTicks >= config.SelfTalkLoopHoldTicks;
        var loopTypeChangedTransition = _selfTalkLoopActive && _loop.LoopTypeChanged && _loopHighTicks >= config.SelfTalkLoopHoldTicks;
        var recoveredLoopTransition = _selfTalkLoopActive && _loopLowTicks >= config.SelfTalkRecoveryHoldTicks;
        var calmWindowEntered = !_calmWindowOpen && _calmWindowTicks >= config.SelfTalkCalmWindowHoldTicks;
        var semanticEvent = ResolveSelfTalkSemanticEvent(enteredLoopTransition, loopTypeChangedTransition, recoveredLoopTransition, calmWindowEntered, resetReason);
        var selfTalk = _selfTalk.MaybeSpeak(new SelfTalkContext(
            enteredLoopTransition,
            loopTypeChangedTransition,
            recoveredLoopTransition,
            calmWindowEntered,
            resetReason == "loop",
            NormalizeLoopType(_loop.LoopType),
            _loop.LoopStrength,
            config.SelfTalkLoopMinStrength
        ), voiceMode, _tick);
        _lastSelfTalk = selfTalk;
        if (!string.IsNullOrWhiteSpace(selfTalk) && _selfTalkThrottle.ShouldSpeak(_tick, selfTalk, semanticEvent))
        {
            _log($"ВНУТРЕННЯЯ РЕЧЬ: {selfTalk}");
            EmitTrace("selftalk", new { text = selfTalk }, ct);
            _output(new LifeOutputDto(_tick, DateTimeOffset.Now, selfTalk, "selftalk"), ct);
            _episodeSelfTalkCount++;
        }
        if (enteredLoopTransition)
            _selfTalkLoopActive = true;
        if (recoveredLoopTransition || resetReason == "loop")
            _selfTalkLoopActive = false;
        if (calmWindowEntered)
            _calmWindowOpen = true;

        var habitUpdated = _habits.UpdateAfter(action.Name, rewardDto.Total, cueKey, _tick);
        if (habitUpdated is not null)
            _log($"ОБУЧЕНИЕ ПРИВЫЧКИ: {RussianDisplay.Token(cueKey)} → " +
                 $"{RussianDisplay.Token(habitUpdated.Id)} сила={habitUpdated.Strength:0.00} " +
                 $"средняя награда={habitUpdated.AvgReward:0.000}");

        if (_tick > 0 && _tick % 200 == 0)
            EmitStats();

        _tick++;
    }

    private void EmitTrace(string stage, object data, CancellationToken ct)
    {
        _trace(new TraceDto(_tick, stage, data), ct);
    }

    private static double ComputeRegulationBonus(ActionDto action, HomeostasisDto before, HomeostasisDto after, AffectDto beforeAffect, AffectDto afterAffect)
    {
        var bonus = 0.0;
        if (action.Name == "breathe_slow" && after.Arousal < before.Arousal)
            bonus += 0.03;
        if (action.Name == "rest_short" && after.Energy > before.Energy)
            bonus += 0.03;
        if (action.Name == "reframe_negative" && afterAffect.Valence > beforeAffect.Valence)
            bonus += 0.02;
        return bonus;
    }

    private static RewardDto ApplyHomeostasisBonus(RewardDto reward, double bonus)
    {
        if (Math.Abs(bonus) < 1e-9) return reward;
        var homeo = reward.Homeostasis + bonus;
        var total = Math.Clamp(
            homeo + reward.Explore + reward.Social + reward.LoopPenalty + reward.InvalidActionPenalty + reward.TerminalPenalty,
            -1.0,
            1.0);
        return reward with { Homeostasis = homeo, Total = total };
    }

    private static IReadOnlyList<DeepBrain.Shared.BrainDtos.V3.GoalDto> ApplyExploreRebound(IReadOnlyList<DeepBrain.Shared.BrainDtos.V3.GoalDto> goals)
    {
        if (goals.Count == 0) return goals;
        var list = goals.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var g = list[i];
            if (g.Id != "explore") continue;
            list[i] = g with { Urgency = LifeMath.Clamp01(g.Urgency + 0.08) };
        }
        return list;
    }

    private DeepBrain.Shared.BrainDtos.V5.PainSourceDto ApplyPainSafetyAdjustments(
        ref HomeostasisDto homeo,
        DeepBrain.Shared.BrainDtos.V3.CircadianDto circ,
        IReadOnlyList<DeepBrain.Shared.BrainDtos.V4.WorldEventDto> eventsList,
        IReadOnlyList<DeepBrain.Shared.BrainDtos.V4.WorldEventDto> currentTickEvents,
        double dtSeconds,
        PainConfig config)
    {
        var threatEvents = eventsList.Where(e => (e.Type == "threat_spike" || e.Type == "micro_threat") && e.Salience > 0.3).ToList();
        var calmEvent = eventsList.Any(e => e.Type == "calm_window" && e.Salience > 0.3);

        var threatComponent = _world.Threat * config.ThreatK + threatEvents.Sum(e => e.Severity) * config.ThreatK;
        var fatigueComponent = Math.Max(0, homeo.Fatigue - 0.6) * config.FatigueK;
        var sleepComponent = Math.Max(0, circ.SleepPressure - 0.7) * config.SleepK;

        var baselinePain = config.BaselinePain;
        var painReturnRatePerSec = config.PainReturnRatePerSec;
        var decay = config.DecayPerSec * dtSeconds;
        var pain = homeo.Pain + dtSeconds * (threatComponent + fatigueComponent + sleepComponent) - decay;
        pain -= (homeo.Pain - baselinePain) * painReturnRatePerSec * dtSeconds;
        if (calmEvent)
            pain -= config.CalmBonusPerSec * dtSeconds;
        if (homeo.Safety > 0.8)
            pain -= config.SafetyBonusPerSec * dtSeconds;

        var safety = SafetyEventIntegrator.Apply(homeo.Safety, currentTickEvents, dtSeconds);

        homeo = homeo with
        {
            Pain = LifeMath.Clamp01(pain),
            Safety = LifeMath.Clamp01(safety)
        };

        var total = threatComponent + fatigueComponent + sleepComponent;
        if (total <= 0)
            return new DeepBrain.Shared.BrainDtos.V5.PainSourceDto(0, 0, 0, 0);

        var posTotal = threatComponent + fatigueComponent + sleepComponent;
        var recovery = Math.Max(0, decay + (calmEvent ? config.CalmBonusPerSec * dtSeconds : 0) + (homeo.Safety > 0.8 ? config.SafetyBonusPerSec * dtSeconds : 0));
        if (posTotal <= 0)
            return new DeepBrain.Shared.BrainDtos.V5.PainSourceDto(0, 0, 0, recovery > 0 ? 1 : 0);

        return new DeepBrain.Shared.BrainDtos.V5.PainSourceDto(
            threatComponent / posTotal,
            fatigueComponent / posTotal,
            sleepComponent / posTotal,
            recovery
        );
    }

    private static string FormatOutputMessage(string message, string actionName, string voiceMode)
    {
        if (actionName == "emit_message")
        {
            if (voiceMode == "tender")
                return "мягкий сигнал связи";
            return "подаю сигнал связи";
        }

        return message;
    }

    private static void ApplyLongTermMemoryBias(List<ActionSelector.Candidate> candidates, MemoryBiasResult recall)
    {
        if (recall.ActionBiases.Count == 0)
            return;

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!recall.ActionBiases.TryGetValue(candidate.Action.Name, out var bias) || Math.Abs(bias) < 0.0005)
                continue;

            var reason = Math.Abs(bias) >= 0.005
                ? $"{candidate.Reason}|long_term_memory"
                : candidate.Reason;
            candidates[i] = candidate with
            {
                Score = candidate.Score + bias,
                Reason = reason
            };
        }
    }

    private static string FormatMemoryEntry(EpisodicMemoryEntry entry)
    {
        var topActions = entry.ActionHistogram
            .OrderByDescending(pair => pair.Value)
            .Take(3)
            .Select(pair => $"{RussianDisplay.Token(pair.Key)}:{pair.Value}");
        return $"эпизод={entry.EpisodeId} время={entry.EndTs:yyyy-MM-dd HH:mm:ss} " +
               $"сценарий={RussianDisplay.Token(entry.ScenarioName)} " +
               $"настроение={RussianDisplay.Token(entry.DominantMood)} " +
               $"тиков={entry.Steps} награда={entry.AvgReward:0.000} " +
               $"петли={entry.LoopCount} пройден={RussianDisplay.YesNo(entry.ScenarioPassed)} " +
               $"повторов={entry.Occurrences} сила={entry.Strength:0.00} " +
               $"действия=[{string.Join(", ", topActions)}]";
    }

    private static string CompactCortexToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "none";

        var compact = value.Trim()
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Replace('\t', '_')
            .Replace(';', '_')
            .Replace('|', '_')
            .Replace('=', '_');
        return compact.Length <= 48 ? compact : compact[..47] + "…";
    }

    private static string FormatCortexNumber(double value) =>
        (double.IsFinite(value) ? value : 0).ToString("0.###", CultureInfo.InvariantCulture);

    private static int ComputeEmitCooldownTicks(string voiceMode, double attachmentBaseline, bool isAnxious, ActionsConfig actions)
    {
        var baseCooldown = voiceMode switch
        {
            "tender" => actions.EmitCooldownTender,
            "fiery" => actions.EmitCooldownFiery,
            "witty" => actions.EmitCooldownWitty,
            _ => actions.EmitCooldownCalm
        };

        var adjust = (0.5 - attachmentBaseline) * 40.0;
        var anxiousBoost = isAnxious ? 20 : 0;
        var value = (int)Math.Round(baseCooldown + adjust + anxiousBoost);
        return Math.Clamp(value, actions.EmitMessage, 160);
    }

    private void AppendEpisode(EpisodeDto episode)
    {
        if (_episodes.Count >= EpisodeCapacity)
            _episodes.RemoveAt(0);
        _episodes.Add(episode);
    }

    private void ResetEpisode(string reason, EpisodeConfig episodeConfig)
    {
        if (reason == "panic")
        {
            var before = _homeo;
            _homeo = PanicRecovery.Restore(_homeo, episodeConfig);
            _log($"ВОССТАНОВЛЕНИЕ ПОСЛЕ ПАНИКИ: безопасность={before.Safety:0.00}→{_homeo.Safety:0.00} " +
                 $"боль={before.Pain:0.00}→{_homeo.Pain:0.00}");
        }

        _episode.Reset(reason);
        _cooldowns.ResetAll();
        _loop.ResetShortTerm();
        _world.ResetEpisode();
        ResetEpisodeMetrics();
        _episodeStartTs = DateTimeOffset.Now;
        _statsTicks = 0;
        _sumPain = 0;
        _sumSafety = 0;
        _sumArousal = 0;
        _sumReward = 0;
        _sumThreat = 0;
        _sumCalm = 0;
        _sumStress = 0;
        _sumThreatFocus = 0;
        _avgReward200 = 0;
        _painClampedCount = 0;
        _painSamples.Clear();
        _actionCounts.Clear();
        _eventCounts.Clear();
        _statsSnapshot = new LifeStatsDto(0, 0, 0, 0);
        _outputSinceDiag = null;
        _loopBreakTicks = 0;
        _loopHighTicks = 0;
        _loopLowTicks = 0;
        _calmWindowTicks = 0;
        _calmWindowInactiveTicks = 0;
        _calmWindowOpen = false;
        _selfTalkLoopActive = false;
    }

    private void UpdateStats(string actionName, string mood, HomeostasisDto homeo, AttentionDto attention, double reward)
    {
        _statsTicks++;
        if (mood == "anxious") _anxiousCount++;
        if (mood == "calm") _calmCount++;
        if (mood == "curious") _curiousCount++;
        if (mood == "neutral") _neutralCount++;
        if (mood == "tender") _tenderCount++;

        _sumPain += homeo.Pain;
        _sumSafety += homeo.Safety;
        _sumArousal += homeo.Arousal;
        _sumReward += reward;
        _sumThreat += _world.Threat;
        _sumCalm += _world.CalmLevel;
        _sumStress += _world.StressLevel;
        _sumThreatFocus += attention.ThreatIntensity;
        if (homeo.Pain >= 0.999) _painClampedCount++;
        _painSamples.Add(homeo.Pain);

        _actionCounts[actionName] = _actionCounts.TryGetValue(actionName, out var count) ? count + 1 : 1;
        _avgReward200 = _statsTicks > 0 ? _sumReward / _statsTicks : 0;
        _statsSnapshot = BuildStatsSnapshot();
    }

    private void UpdateEpisodeMetrics(ActionDto action, string mood, RewardDto reward, bool loopDetected, double loopStrength, HomeostasisDto homeo, bool socialContactHandled, bool maskFallback, bool invalidAction, bool usedMl, double epsilon)
    {
        _episodeSteps++;
        _episodeRewardSum += reward.Total;
        _episodeRewardBreakdown = new RewardDto(
            _episodeRewardBreakdown.Homeostasis + reward.Homeostasis,
            _episodeRewardBreakdown.Explore + reward.Explore,
            _episodeRewardBreakdown.Social + reward.Social,
            _episodeRewardBreakdown.LoopPenalty + reward.LoopPenalty,
            _episodeRewardBreakdown.InvalidActionPenalty + reward.InvalidActionPenalty,
            _episodeRewardBreakdown.Total + reward.Total
        )
        {
            TerminalPenalty = _episodeRewardBreakdown.TerminalPenalty + reward.TerminalPenalty
        };

        _episodeActionCounts[action.Name] = _episodeActionCounts.TryGetValue(action.Name, out var count) ? count + 1 : 1;
        _episodeActionRewardSums[action.Name] =
            _episodeActionRewardSums.TryGetValue(action.Name, out var rewardSum) ? rewardSum + reward.Total : reward.Total;
        _episodeMoodCounts[mood] = _episodeMoodCounts.TryGetValue(mood, out var mcount) ? mcount + 1 : 1;

        if (loopDetected && !_episodeLoopActive)
        {
            _episodeLoopCount++;
            _episodeLoopActive = true;
        }
        if (!loopDetected)
            _episodeLoopActive = false;
        _episodeMaxLoopStrength = Math.Max(_episodeMaxLoopStrength, loopStrength);

        _episodeSumPain += homeo.Pain;
        _episodeMaxPain = Math.Max(_episodeMaxPain, homeo.Pain);
        _episodeSumSafety += homeo.Safety;
        _episodeSumArousal += homeo.Arousal;

        if (action.Name == "emit_message" && socialContactHandled)
            _episodeSocialSignals++;
        if (maskFallback)
            _episodeMaskFallbackCount++;
        if (invalidAction)
            _episodeInvalidActionCount++;
        if (usedMl)
            _episodeMlUsed = true;
        _episodeLastEpsilon = epsilon;
    }

    private void ResetEpisodeMetrics()
    {
        _episodeSteps = 0;
        _episodeRewardSum = 0;
        _episodeRewardBreakdown = new RewardDto(0, 0, 0, 0, 0, 0);
        _episodeActionCounts.Clear();
        _episodeActionRewardSums.Clear();
        _episodeMoodCounts.Clear();
        _episodeLoopCount = 0;
        _episodeLoopActive = false;
        _episodeMaxLoopStrength = 0;
        _episodeSumPain = 0;
        _episodeMaxPain = 0;
        _episodeSumSafety = 0;
        _episodeSumArousal = 0;
        _episodeSocialSignals = 0;
        _episodeSelfTalkCount = 0;
        _episodeMaskFallbackCount = 0;
        _episodeInvalidActionCount = 0;
        _episodeMlUsed = false;
        _episodeLastEpsilon = 0;
    }

    private EpisodeReport BuildEpisodeReport(string reason, string scenarioName, string backendKind)
    {
        var steps = Math.Max(1, _episodeSteps);
        var rewardAvg = new RewardDto(
            _episodeRewardBreakdown.Homeostasis / steps,
            _episodeRewardBreakdown.Explore / steps,
            _episodeRewardBreakdown.Social / steps,
            _episodeRewardBreakdown.LoopPenalty / steps,
            _episodeRewardBreakdown.InvalidActionPenalty / steps,
            _episodeRewardBreakdown.Total / steps
        )
        {
            TerminalPenalty = _episodeRewardBreakdown.TerminalPenalty / steps
        };

        var report = new EpisodeReport(
            EpisodeId: _episode.EpisodeId,
            ScenarioName: scenarioName,
            StartTs: _episodeStartTs,
            EndTs: DateTimeOffset.Now,
            Steps: steps,
            EndReason: reason,
            AvgReward: _episodeRewardSum / steps,
            TotalReward: _episodeRewardSum,
            RewardBreakdownAvg: rewardAvg,
            ActionHistogram: new Dictionary<string, int>(_episodeActionCounts, StringComparer.Ordinal),
            LoopCount: _episodeLoopCount,
            MaxLoopStrength: _episodeMaxLoopStrength,
            MoodDistribution: new Dictionary<string, int>(_episodeMoodCounts, StringComparer.OrdinalIgnoreCase),
            AvgPain: _episodeSumPain / steps,
            MaxPain: _episodeMaxPain,
            AvgSafety: _episodeSumSafety / steps,
            AvgArousal: _episodeSumArousal / steps,
            SocialSignalsSent: _episodeSocialSignals,
            SelfTalkCount: _episodeSelfTalkCount,
            MaskFallbackCount: _episodeMaskFallbackCount,
            InvalidActionCount: _episodeInvalidActionCount,
            MlUsed: _episodeMlUsed,
            BackendKind: backendKind,
            EpsilonUsed: _episodeLastEpsilon,
            ScenarioScore: null
        )
        {
            ActionRewardAverages = _episodeActionRewardSums.ToDictionary(
                pair => pair.Key,
                pair => pair.Value / Math.Max(1, _episodeActionCounts.TryGetValue(pair.Key, out var count) ? count : 1),
                StringComparer.Ordinal)
        };

        var score = _scenarioScorer.Score(scenarioName, report);
        return report with { ScenarioScore = score };
    }

    private static DeepBrain.Shared.BrainDtos.V6.EvaluationSnapshotDto ToEvaluationDto(EvaluationSnapshot snapshot)
    {
        return new DeepBrain.Shared.BrainDtos.V6.EvaluationSnapshotDto(
            snapshot.AvgReward,
            snapshot.MedianReward,
            snapshot.SuccessRate,
            snapshot.AvgEpisodeLength,
            Math.Clamp(snapshot.LoopRate, 0.0, 1.0),
            Math.Clamp(snapshot.ActionDiversity, 0.0, 1.0),
            Math.Clamp(snapshot.CalmRatio, 0.0, 1.0),
            Math.Clamp(snapshot.AnxiousRatio, 0.0, 1.0),
            Math.Clamp(snapshot.CuriousRatio, 0.0, 1.0),
            Math.Clamp(snapshot.InvalidActionRate, 0.0, 1.0),
            Math.Clamp(snapshot.MaskFallbackRate, 0.0, 1.0),
            snapshot.EpisodeCount,
            Math.Max(0, snapshot.CalmCount),
            Math.Max(0, snapshot.AnxiousCount),
            Math.Max(0, snapshot.CuriousCount),
            Math.Max(0, snapshot.LoopCount)
        );
    }

    private static string FormatRewardTraceLine(RewardDto reward)
    {
        return $"награда: всего={FormatSigned(reward.Total)} " +
               $"гомео={FormatSigned(reward.Homeostasis)} " +
               $"исслед={FormatSigned(reward.Explore)} " +
               $"соц={FormatSigned(reward.Social)} " +
               $"петля={FormatSigned(reward.LoopPenalty)} " +
               $"недоп={FormatSigned(reward.InvalidActionPenalty)} " +
               $"терминал={FormatSigned(reward.TerminalPenalty)}";
    }

    private static string NormalizeDecisionReason(string reason, bool maskFallback)
    {
        if (maskFallback && string.Equals(reason, "mask_fallback", StringComparison.Ordinal))
            return "no_allowed_actions";
        return reason;
    }

    private static string? ResolveSelfTalkSemanticEvent(bool enteredLoopTransition, bool loopTypeChangedTransition, bool recoveredLoopTransition, bool calmWindowEntered, string resetReason)
    {
        if (resetReason == "loop")
            return "episode_loop_end";
        if (enteredLoopTransition)
            return "entered_loop";
        if (loopTypeChangedTransition)
            return "loop_type_changed";
        if (recoveredLoopTransition)
            return "recovered_from_loop";
        if (calmWindowEntered)
            return "entered_calm_window";
        return null;
    }

    private static string FormatSigned(double value)
    {
        return value >= 0 ? $"+{value:0.00}" : $"{value:0.00}";
    }

    private static string NormalizeLoopType(string? loopType)
    {
        return string.IsNullOrWhiteSpace(loopType) ? "none" : loopType;
    }

    private string ResolveMlMode(MlConfig config)
    {
        var mode = string.IsNullOrWhiteSpace(_mlModeOverride) ? config.Mode : _mlModeOverride;
        if (string.IsNullOrWhiteSpace(mode)) return "training";
        mode = mode.Trim().ToLowerInvariant();
        return mode is "evaluation" or "training" ? mode : "training";
    }

    private LifeStatsDto BuildStatsSnapshot()
    {
        if (_statsTicks == 0) return _statsSnapshot;
        var anxiousPct = _anxiousCount / (double)_statsTicks;
        var calmPct = _calmCount / (double)_statsTicks;
        var curiousPct = _curiousCount / (double)_statsTicks;
        var p95Pain = ComputeP95(_painSamples);
        return new LifeStatsDto(anxiousPct, calmPct, curiousPct, p95Pain);
    }

    private void EmitStats()
    {
        if (_statsTicks == 0) return;

        var anxiousPct = _anxiousCount / (double)_statsTicks;
        var calmPct = _calmCount / (double)_statsTicks;
        var curiousPct = _curiousCount / (double)_statsTicks;
        var neutralPct = _neutralCount / (double)_statsTicks;
        var tenderPct = _tenderCount / (double)_statsTicks;
        var avgPain = _sumPain / _statsTicks;
        var avgSafety = _sumSafety / _statsTicks;
        var avgArousal = _sumArousal / _statsTicks;
        var avgThreat = _sumThreat / _statsTicks;
        var avgCalm = _sumCalm / _statsTicks;
        var avgStress = _sumStress / _statsTicks;
        var avgThreatFocus = _sumThreatFocus / _statsTicks;
        var p95Pain = ComputeP95(_painSamples);

        var topActions = _actionCounts
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => $"{RussianDisplay.Token(kv.Key)}:{kv.Value}")
            .ToArray();

        var threatCount = _eventCounts.TryGetValue("threat_spike", out var t) ? t : 0;
        var microCount = _eventCounts.TryGetValue("micro_threat", out var m) ? m : 0;
        var calmCount = _eventCounts.TryGetValue("calm_window", out var c) ? c : 0;
        var novCount = _eventCounts.TryGetValue("novelty_opportunity", out var n) ? n : 0;
        var socialCount = _eventCounts.TryGetValue("social_ping", out var s) ? s : 0;

        _log($"СТАТИСТИКА(200): тревога={anxiousPct:0.00} спокойствие={calmPct:0.00} " +
             $"любопытство={curiousPct:0.00} нейтральное={neutralPct:0.00} мягкое={tenderPct:0.00} " +
             $"средняя боль={avgPain:0.00} боль p95={p95Pain:0.00} " +
             $"средняя безопасность={avgSafety:0.00} среднее возбуждение={avgArousal:0.00}");
        _log($"СТАТИСТИКА климата: средняя угроза={avgThreat:0.00} " +
             $"среднее спокойствие={avgCalm:0.00} средний стресс={avgStress:0.00} " +
             $"фокус на угрозе={avgThreatFocus:0.00} ограничений боли={_painClampedCount}");
        _log($"СТАТИСТИКА действий: {string.Join(", ", topActions)}");
        _log($"СТАТИСТИКА событий: всплеск угрозы={threatCount} малая угроза={microCount} " +
             $"окно спокойствия={calmCount} новизна={novCount} сигнал контакта={socialCount}");
        if (_mlTelemetry is not null)
        {
            _log($"СТАТИСТИКА_ML(200): средняя награда={_mlTelemetry.AvgReward200:0.000} " +
                 $"среднее Q={_mlTelemetry.AvgQ:0.000} энтропия={_mlTelemetry.Entropy:0.000} " +
                 $"ε={_mlTelemetry.Epsilon:0.000} вес сети={_mlTelemetry.NetWeight:0.00} " +
                 $"ошибка={_mlTelemetry.LastLoss:0.000} буфер={_mlTelemetry.BufferSize} " +
                 $"пропуски NaN={_mlTelemetry.NanSkips} " +
                 $"недопустимые={_mlTelemetry.InvalidActionFallbackCount} " +
                 $"источник={RussianDisplay.Token(_mlTelemetry.PolicySource)}");
        }

        _anxiousCount = 0;
        _calmCount = 0;
        _curiousCount = 0;
        _neutralCount = 0;
        _tenderCount = 0;
        _statsTicks = 0;
        _sumPain = 0;
        _sumSafety = 0;
        _sumArousal = 0;
        _sumReward = 0;
        _sumThreat = 0;
        _sumCalm = 0;
        _sumStress = 0;
        _sumThreatFocus = 0;
        _painClampedCount = 0;
        _painSamples.Clear();
        _actionCounts.Clear();
        _eventCounts.Clear();
        _statsSnapshot = new LifeStatsDto(0, 0, 0, 0);
        _avgReward200 = 0;
    }

    private static double ComputeP95(List<double> samples)
    {
        if (samples.Count == 0) return 0;
        var ordered = samples.OrderBy(v => v).ToList();
        var index = (int)Math.Floor(0.95 * (ordered.Count - 1));
        return ordered[index];
    }

    private static bool IsMaskAllowed(float[] mask, string actionName)
    {
        var idx = ActionCatalog.IndexOf(actionName);
        if (idx < 0 || idx >= mask.Length) return true;
        return mask[idx] > 0f;
    }

    private static float[] BuildCandidateMask(float[] environmentMask, IReadOnlyCollection<string> candidateActions)
    {
        var candidateSet = new HashSet<string>(candidateActions, StringComparer.Ordinal);
        var result = new float[ActionCatalog.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var environmentAllows = i < environmentMask.Length && environmentMask[i] > 0f;
            result[i] = environmentAllows && candidateSet.Contains(ActionCatalog.Actions[i]) ? 1f : 0f;
        }
        return result;
    }

    private static void TryDeleteCheckpoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            var weights = path + ".net";
            if (File.Exists(weights))
                File.Delete(weights);
        }
        catch
        {
        }
    }
}
