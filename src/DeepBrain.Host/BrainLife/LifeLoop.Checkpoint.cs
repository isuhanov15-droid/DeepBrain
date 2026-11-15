using System.Text.Json;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed partial class LifeLoop
{
    private readonly object _runtimeGate = new();
    private readonly ForesightPlanner _foresight = new();
    private bool _atCompletedBoundary = true;
    private bool _checkpointRestored;
    private string? _checkpointId;
    private DateTimeOffset? _checkpointSavedAt;
    private long? _checkpointTick;
    private string? _restoredCheckpointId;
    private long? _restoredTick;
    private string? _continuityConfigHash;

    private Dictionary<string, (Func<JsonElement> Capture, Action<JsonElement> Restore)> RuntimeComponents()
    {
        var components = new Dictionary<string, (Func<JsonElement>, Action<JsonElement>)>(StringComparer.Ordinal)
        {
            ["life"] = (CaptureRuntimeState, RestoreRuntimeState),
            ["world"] = (_world.CaptureRuntimeState, _world.RestoreRuntimeState),
            ["events"] = (_world.Events.CaptureRuntimeState, _world.Events.RestoreRuntimeState),
            ["clock"] = (_clock.CaptureRuntimeState, _clock.RestoreRuntimeState),
            ["sleep"] = (_sleep.CaptureRuntimeState, _sleep.RestoreRuntimeState),
            ["episode"] = (_episode.CaptureRuntimeState, _episode.RestoreRuntimeState),
            ["recentMemory"] = (_memory.CaptureRuntimeState, _memory.RestoreRuntimeState),
            ["learning"] = (_learning.CaptureRuntimeState, _learning.RestoreRuntimeState),
            ["loops"] = (_loop.CaptureRuntimeState, _loop.RestoreRuntimeState),
            ["mood"] = (_inertia.CaptureRuntimeState, _inertia.RestoreRuntimeState),
            ["attention"] = (_attentionInertia.CaptureRuntimeState, _attentionInertia.RestoreRuntimeState),
            ["semantic"] = (_semantic.CaptureRuntimeState, _semantic.RestoreRuntimeState),
            ["selfTalk"] = (_selfTalkThrottle.CaptureRuntimeState, _selfTalkThrottle.RestoreRuntimeState),
            ["cooldowns"] = (_cooldowns.CaptureRuntimeState, _cooldowns.RestoreRuntimeState),
            ["dedupe"] = (_dedupe.CaptureRuntimeState, _dedupe.RestoreRuntimeState),
            ["goals"] = (_goals.CaptureRuntimeState, _goals.RestoreRuntimeState),
            ["legacyPlan"] = (_plan.CaptureRuntimeState, _plan.RestoreRuntimeState),
            ["curriculum"] = (_curriculum.CaptureRuntimeState, _curriculum.RestoreRuntimeState),
            ["evaluation"] = (_policyEvaluator.CaptureRuntimeState, _policyEvaluator.RestoreRuntimeState),
            ["moodStats"] = (_moodStats.CaptureRuntimeState, _moodStats.RestoreRuntimeState),
            ["executive"] = (_executive.CaptureRuntimeState, _executive.RestoreRuntimeState),
            ["cognition"] = (_cognition.CaptureRuntimeState, _cognition.RestoreRuntimeState),
            ["conversation"] = (_conversation.CaptureRuntimeState, _conversation.RestoreRuntimeState),
            ["habits"] = (_habits.CaptureRuntimeState, _habits.RestoreRuntimeState),
            ["longTermMemory"] = (_longTermMemory.CaptureRuntimeState, _longTermMemory.RestoreRuntimeState)
        };
        if (_ml is MlPolicyAdvisor advisor)
            components.Add("mlPolicy", (advisor.CaptureRuntimeState, advisor.RestoreRuntimeState));
        if (_foresight.Enabled)
            components.Add("foresight", (_foresight.CaptureRuntimeState, _foresight.RestoreRuntimeState));
        return components;
    }

    private Dictionary<string, JsonElement> CaptureRuntimeComponents() => RuntimeComponents()
        .ToDictionary(x => x.Key, x => x.Value.Capture(), StringComparer.Ordinal);

    private void InitializeContinuity(BrainConfig config, RuntimeCheckpoint? checkpoint)
    {
        if (config.Continuity?.Enable != true) return;
        _continuityConfigHash = RuntimeCheckpoint.ConfigurationHash(config);
        _world.EnableContinuityRandom();
        _curriculum.EnableContinuityRandom();
        if (_ml is MlPolicyAdvisor advisor) advisor.EnableContinuityRandom();
        if (checkpoint is null) return;
        var components = RuntimeComponents();
        var restoredComponents = components.Where(c =>
            (checkpoint.SchemaVersion >= 2 || c.Key != "foresight") &&
            (checkpoint.SchemaVersion >= 3 || c.Key != "conversation")).ToDictionary(c=>c.Key,c=>c.Value);
        if (!restoredComponents.Keys.Order().SequenceEqual(checkpoint.Components.Keys.Order()))
            throw new InvalidDataException("Incomplete runtime component set");
        foreach (var component in restoredComponents)
            component.Value.Restore(checkpoint.Components[component.Key]);
        if (_tick < 1 || _episodeSteps < 0 || _episodes.Count > EpisodeCapacity ||
            new[] { _homeo.Energy, _homeo.Fatigue, _homeo.Arousal, _homeo.Pain, _homeo.Safety }.Any(x => x < 0 || x > 1) ||
            _pendingMlTransition is { } transition && (transition.State.Length != StateVectorizer.InputDim ||
                transition.Action < 0 || transition.Action >= ActionCatalog.Count))
            throw new InvalidDataException("Invalid runtime body, episode or learning transition");
        var restoredHash = RuntimeCheckpoint.HashState(CaptureRuntimeComponents()
            .Where(p => restoredComponents.ContainsKey(p.Key)).ToDictionary(p => p.Key,p => p.Value));
        if (restoredHash != checkpoint.StateHash)
            throw new InvalidDataException("Runtime round-trip did not reproduce the saved boundary");
        if (config.Ml.Enable || config.UseMlAdvisor)
        {
            if (!_ml.TryLoad(config.Ml.CheckpointPath, out var episodeId) || episodeId != _episode.EpisodeId)
                throw new InvalidDataException("Could not restore ML at the saved episode boundary");
            _mlLoaded = true;
            // Loading the backend establishes its connection; restore the policy's
            // counters and random stream again, without restoring old connections.
            if (_ml is MlPolicyAdvisor policy) policy.RestoreRuntimeState(checkpoint.Components["mlPolicy"]);
        }
        if (RuntimeCheckpoint.CaptureFiles(config).Any(p => checkpoint.Files[p.Key] != p.Value))
            throw new InvalidDataException("Persistent files changed during restore; refusing to advance the agent.");
        _checkpointRestored = true;
        _checkpointId = _restoredCheckpointId = checkpoint.Id;
        _checkpointSavedAt = checkpoint.SavedAt;
        _checkpointTick = _restoredTick = _tick;
        RuntimeCheckpoint.AtomicWrite(config.Continuity.Path + ".restore.json", new
        {
            checkpointId = checkpoint.Id, runId = _runId, restoredAt = DateTimeOffset.UtcNow,
            sourceSchemaVersion = checkpoint.SchemaVersion, migratedToSchemaVersion = 3,
            expectedStateHash = checkpoint.StateHash, restoredStateHash = restoredHash,
            nextTick = _tick, episodeId = _episode.EpisodeId, episodeTick = _episode.EpisodeTick,
            homeostasis = _homeo, affect = _affect, circadian = _clock.Snapshot(_sleep.IsSleeping),
            scenario = _curriculum.ScenarioName, pendingLearningTransition = _pendingMlTransition is not null,
            executive = _executive.Snapshot(), events = checkpoint.Components["events"]
        });
        _log($"СОСТОЯНИЕ ВОССТАНОВЛЕНО: снимок={checkpoint.Id} следующий тик={_tick} эпизод={_episode.EpisodeId}");
    }

    private ContinuityStateDto? ContinuityStatus()
    {
        if (_continuityConfigHash is null) return null;
        return new(true, _checkpointRestored, _checkpointId, _checkpointSavedAt, _checkpointTick, _restoredCheckpointId, _restoredTick);
    }

    private void SaveRuntimeCheckpoint(BrainConfig config)
    {
        if (config.Continuity?.Enable != true || _tick == 0) return;
        if (!_atCompletedBoundary) throw new InvalidOperationException("Cannot save a partially completed tick. Previous checkpoint retained.");
        if (_continuityConfigHash != RuntimeCheckpoint.ConfigurationHash(config))
            throw new InvalidOperationException("Configuration changed during a continuity run. Previous checkpoint retained.");
        _executive.Flush(); _executive.EnsureContinuitySaved();
        _cognition.Flush(); _cognition.EnsureContinuitySaved();
        _habits.Flush(); _habits.EnsureContinuitySaved();
        if (config.Ml.Enable || config.UseMlAdvisor)
        {
            if (!_mlLoaded) throw new InvalidOperationException("ML has not loaded; refusing incomplete runtime checkpoint");
            _ml.TrySave(config.Ml.CheckpointPath, _episode.EpisodeId);
        }
        var components = CaptureRuntimeComponents();
        var checkpoint = new RuntimeCheckpoint
        {
            Id = Guid.NewGuid().ToString("N"), SavedAt = DateTimeOffset.UtcNow,
            ConfigHash = _continuityConfigHash!, StateHash = RuntimeCheckpoint.HashState(components),
            Components = components, Files = RuntimeCheckpoint.CaptureFiles(config)
        };
        if (!File.Exists(config.Continuity.Path + ".required"))
            RuntimeCheckpoint.AtomicWrite(config.Continuity.Path + ".required", new { schemaVersion = 1 });
        RuntimeCheckpoint.AtomicWrite(config.Continuity.Path, checkpoint);
        _checkpointId = checkpoint.Id; _checkpointSavedAt = checkpoint.SavedAt; _checkpointTick = _tick;
    }

    private void TickOnce(double dtSeconds, CancellationToken ct)
    {
        lock (_runtimeGate)
        {
            var (config, _) = _configLoader.GetCurrent();
            if (_continuityConfigHash is not null && _continuityConfigHash != RuntimeCheckpoint.ConfigurationHash(config))
                throw new InvalidOperationException("Configuration changed during a continuity run; stop and inspect before resuming.");
            _atCompletedBoundary = false;
            TickCore(dtSeconds, ct);
            _stepRequested = false;
            _atCompletedBoundary = true;
            if (config.Continuity?.Enable == true && _tick % config.Continuity.SaveEveryTicks == 0)
                SaveRuntimeCheckpoint(config);
        }
    }
}
