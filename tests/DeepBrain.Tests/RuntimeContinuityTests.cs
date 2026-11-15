using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeepBrain.Host.BrainLife;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.Brain;
using Xunit;

namespace DeepBrain.Tests;

public sealed partial class RuntimeContinuityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "deepbrain-continuity-" + Guid.NewGuid().ToString("N"));
    public RuntimeContinuityTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);
    private string FilePath(string name) => Path.Combine(_directory, name);
    private BrainConfig Config => BrainConfig.Default with
    {
        ScenarioDefault = "novelty_walk",
        Planning = new() { Enable = true, EnableIntentions = true, EnableStrategyLearning = true, Path = FilePath("planning.json") },
        CognitiveDynamics = new() { Enable = true, Path = FilePath("cognition.json") },
        Continuity = new(true, FilePath("runtime.json"), 100_000),
        Ml = BrainConfig.Default.Ml with { Enable = false, Backend = "off" },
        Memory = MemoryConfig.Default with { Enable = true, Path = FilePath("episodes.jsonl"), MinEpisodesBeforeBias = 100_000 },
        Habits = HabitConfig.Default with { Enable = true, Path = FilePath("habits.json"), ImportPath = "" },
        Llm = LlmConfig.Default with { Enable = false, PersistJournal = false },
        Evaluation = BrainConfig.Default.Evaluation with { SaveReports = false }
    };
    private (LifeLoop Loop, List<LifeStateDto> States) NewLoop(BrainConfig? config = null, Action? onBroadcast = null, IMlPolicyAdvisor? ml = null)
    {
        var path = FilePath("config.json"); File.WriteAllText(path, JsonSerializer.Serialize(config ?? Config));
        var states = new List<LifeStateDto>();
        return (new LifeLoop(new WorldSim(1337), new HomeostasisEngine(), new InstinctEngine(), new EmotionEngine(),
            new ActionSelector(), new Actuator(), new RewardEngine(), new LearningEngine(), new BrainConfigLoader(path, _ => { }),
            (s, _) => { states.Add(s); onBroadcast?.Invoke(); }, (_, _) => { }, (_, _) => { }, _ => { }, ml), states);
    }
    private static void Tick(LifeLoop loop, int count = 1)
    {
        var method = typeof(LifeLoop).GetMethod("TickOnce", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < count; i++) method.Invoke(loop, new object[] { 0.2, CancellationToken.None });
    }
    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object Get(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private string RuntimeState(LifeLoop loop) => JsonSerializer.Serialize(typeof(LifeLoop)
        .GetMethod("CaptureRuntimeComponents", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(loop, null));
    private Dictionary<string, byte[]> Backup() => Directory.GetFiles(_directory).ToDictionary(x => x, System.IO.File.ReadAllBytes);
    private static void Restore(Dictionary<string, byte[]> files) { foreach (var p in files) System.IO.File.WriteAllBytes(p.Key, p.Value); }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FullBoundaryAndNextActionsSurviveAwakeAndSleepingRestart(bool sleeping, bool foresight)
    {
        var config = Config with { Foresight = foresight ? new(true) : null };
        var first = NewLoop(config); Tick(first.Loop, 130);
        if (sleeping)
        {
            var clock = Get(first.Loop, "_clock");
            clock.GetType().GetProperty("TimeOfDay")!.SetValue(clock, .90);
            clock.GetType().GetProperty("SleepPressure")!.SetValue(clock, .80);
            Set(first.Loop, "_homeo", new HomeostasisDto(.7, .6, .3, .1, .9));
            Tick(first.Loop);
            Assert.True(first.States[^1].Circadian!.IsSleeping);
        }
        first.Loop.FlushPersistentState();
        var expectedBoundary = RuntimeState(first.Loop);
        var backup = Backup();
        var expected = new List<LifeStateDto>();
        for (var i = 0; i < 400; i++) { Tick(first.Loop); expected.Add(first.States[^1]); }
        if (sleeping) Assert.Contains(expected, x => x.Circadian!.IsSleeping == false);
        Restore(backup);
        var restored = NewLoop(config);
        Assert.Equal(expectedBoundary, RuntimeState(restored.Loop));
        for (var i = 0; i < expected.Count; i++)
        {
            Tick(restored.Loop); var actual = restored.States[^1]; var wanted = expected[i];
            Assert.Equal(wanted.Tick, actual.Tick);
            Assert.Equal(wanted.Homeostasis, actual.Homeostasis);
            Assert.Equal(wanted.Affect, actual.Affect);
            Assert.Equal(wanted.Circadian, actual.Circadian);
            Assert.Equal(wanted.LastDecision, actual.LastDecision);
            Assert.Equal(wanted.LastReward, actual.LastReward);
            Assert.Equal(wanted.Climate, actual.Climate);
            Assert.Equal(wanted.Episode, actual.Episode);
            Assert.Equal(wanted.ActivePlan?.AgeTicks, actual.ActivePlan?.AgeTicks);
            Assert.Equal(wanted.ActivePlan?.Progress, actual.ActivePlan?.Progress);
            Assert.True(actual.Continuity!.Restored);
        }
        var proof = JsonNode.Parse(System.IO.File.ReadAllText(FilePath("runtime.json.restore.json")))!;
        Assert.Equal(proof["expectedStateHash"]!.ToString(), proof["restoredStateHash"]!.ToString());
    }

    [Fact]
    public void Version19CheckpointMigratesWithoutChangingExistingRuntimeOrExperience()
    {
        var first = NewLoop(); Tick(first.Loop, 85); first.Loop.FlushPersistentState();
        var old = JsonNode.Parse(RuntimeState(first.Loop))!.AsObject(); old.Remove("conversation");
        var json = JsonNode.Parse(System.IO.File.ReadAllText(FilePath("runtime.json")))!;
        json["schemaVersion"] = 1; // v1.9's same component/config contract, without foresight.
        RemoveConversationFromLegacy(json);
        System.IO.File.WriteAllText(FilePath("runtime.json"), json.ToJsonString());
        var backup = Backup();
        var config = Config with { Foresight = new(true) };
        var restored = NewLoop(config);
        var actual = JsonNode.Parse(RuntimeState(restored.Loop))!.AsObject();
        Assert.True(actual.Remove("foresight"));
        Assert.True(actual.Remove("conversation"));
        Assert.True(JsonNode.DeepEquals(old,actual));
        foreach(var file in backup.Where(p => !p.Key.EndsWith("config.json")))
            Assert.Equal(file.Value,System.IO.File.ReadAllBytes(file.Key));
        Tick(restored.Loop, 5); restored.Loop.FlushPersistentState();
        var upgraded = JsonNode.Parse(System.IO.File.ReadAllText(FilePath("runtime.json")))!;
        Assert.Equal(3, upgraded["schemaVersion"]!.GetValue<int>());
        Assert.NotNull(upgraded["components"]!["foresight"]);
        var again = NewLoop(config);
        Assert.Equal(RuntimeState(restored.Loop),RuntimeState(again.Loop));
    }

    [Fact]
    public void LegacyMigrationDoesNotPermitUnrelatedConfigurationChanges()
    {
        var run=NewLoop();Tick(run.Loop,10);run.Loop.FlushPersistentState();
        var json=JsonNode.Parse(System.IO.File.ReadAllText(FilePath("runtime.json")))!;json["schemaVersion"]=1;
        RemoveConversationFromLegacy(json);
        System.IO.File.WriteAllText(FilePath("runtime.json"),json.ToJsonString());
        var backup=Backup();
        Assert.Throws<InvalidDataException>(()=>NewLoop(Config with { Foresight=new(true), TickRate=10 }));
        foreach(var file in backup.Where(p=>!p.Key.EndsWith("config.json")))Assert.Equal(file.Value,System.IO.File.ReadAllBytes(file.Key));
    }

    [Fact]
    public void CheckpointIncludesConsumedEventsRecentLearningAndCooldowns()
    {
        var first = NewLoop(); Tick(first.Loop, 350); first.Loop.FlushPersistentState();
        var state = JsonNode.Parse(System.IO.File.ReadAllText(FilePath("runtime.json")))!["components"]!;
        Assert.NotEmpty(state["events"]!["events"]!.AsArray());
        Assert.NotEmpty(state["recentMemory"]!["episodes"]!.AsArray());
        Assert.NotEmpty(state["learning"]!["emaReward"]!.AsObject());
        Assert.NotEmpty(state["cooldowns"]!["lastTick"]!.AsObject());
        var restored = NewLoop();
        Assert.Equal(RuntimeState(first.Loop), RuntimeState(restored.Loop));
    }

    [Theory]
    [InlineData("damaged")]
    [InlineData("missing_component")]
    [InlineData("changed_memory")]
    [InlineData("changed_config")]
    [InlineData("missing_checkpoint")]
    public void InconsistentRestoreStopsWithoutChangingAnyPersistentFile(string damage)
    {
        var first = NewLoop(); Tick(first.Loop, 20); first.Loop.FlushPersistentState();
        if (damage == "damaged") System.IO.File.WriteAllText(FilePath("runtime.json"), "{");
        if (damage == "missing_checkpoint") System.IO.File.Delete(FilePath("runtime.json"));
        if (damage == "missing_component")
        {
            var json = JsonNode.Parse(System.IO.File.ReadAllText(FilePath("runtime.json")))!;
            json["components"]!.AsObject().Remove("clock");
            System.IO.File.WriteAllText(FilePath("runtime.json"), json.ToJsonString());
        }
        if (damage == "changed_memory") System.IO.File.AppendAllText(FilePath("habits.json"), " ");
        var backup = Backup();
        Assert.ThrowsAny<Exception>(() => NewLoop(damage == "changed_config" ? Config with { TickRate = 10 } : Config));
        foreach (var p in backup.Where(x => !x.Key.EndsWith("config.json"))) Assert.Equal(p.Value, System.IO.File.ReadAllBytes(p.Key));
    }

    [Fact]
    public void MidTickFailureDoesNotReplaceLastCompletedCheckpoint()
    {
        var fail = false;
        var run = NewLoop(onBroadcast: () => { if (fail) throw new IOException("test broadcast failure"); });
        Tick(run.Loop, 5); run.Loop.FlushPersistentState();
        var previous = System.IO.File.ReadAllBytes(FilePath("runtime.json"));
        fail = true; Assert.Throws<TargetInvocationException>(() => Tick(run.Loop));
        Assert.Throws<InvalidOperationException>(() => run.Loop.FlushPersistentState());
        Assert.Equal(previous, System.IO.File.ReadAllBytes(FilePath("runtime.json")));
    }

    [Fact]
    public void PeriodicSaveOccursAfterCompletedTickAndPausedStateRestores()
    {
        var config = Config with { Continuity = Config.Continuity! with { SaveEveryTicks = 5 } };
        var run = NewLoop(config); Tick(run.Loop, 5);
        var json = JsonNode.Parse(System.IO.File.ReadAllText(FilePath("runtime.json")))!;
        Assert.Equal(5, json["components"]!["life"]!["tick"]!.GetValue<int>());
        run.Loop.Stop(); run.Loop.FlushPersistentState();
        var restored = NewLoop(config);
        Assert.False((bool)Get(restored.Loop, "_running"));
        Assert.Equal(RuntimeState(run.Loop), RuntimeState(restored.Loop));
    }

    [Fact]
    public void PendingLearningTransitionSurvivesRestartAndEntersReplayExactlyOnce()
    {
        var config = Config with { Ml = Config.Ml with { Enable = true, Backend = "remote", CheckpointPath = FilePath("ml.chk") } };
        var beforeBackend = new ReplayBackend();
        var before = NewLoop(config, ml: new MlPolicyAdvisor(config.Ml, _ => { }, beforeBackend));
        Tick(before.Loop, 12); before.Loop.FlushPersistentState();
        var saved = JsonNode.Parse(System.IO.File.ReadAllText(FilePath("runtime.json")))!["components"]!["life"]!["pendingMlTransition"]!;
        Assert.NotNull(saved);
        var replayCount = beforeBackend.Replay.Count;
        var afterBackend = new ReplayBackend();
        var after = NewLoop(config, ml: new MlPolicyAdvisor(config.Ml, _ => { }, afterBackend));
        Assert.Equal(RuntimeState(before.Loop), RuntimeState(after.Loop));
        Assert.Equal(replayCount, afterBackend.Replay.Count);
        Tick(after.Loop);
        Assert.Equal(replayCount + 1, afterBackend.Replay.Count);
        var transition = afterBackend.Replay[^1];
        Assert.Equal(saved["state"]!.Deserialize<float[]>(), transition.State);
        Assert.Equal(saved["action"]!.GetValue<int>(), transition.Action);
        Tick(after.Loop);
        Assert.Equal(replayCount + 2, afterBackend.Replay.Count);
    }

    private sealed class ReplayBackend : IBrainMlBackend
    {
        public List<Transition> Replay { get; private set; } = new();
        public bool IsAvailable => true;
        public string Kind => "remote";
        public bool IsConnected => true;
        public string? LastError => null;
        public double LastRttMs => 0;
        public int InputDim => StateVectorizer.InputDim;
        public int ActionCount => ActionCatalog.Count;
        public int BufferSize => Replay.Count;
        public int BufferCapacity => 20_000;
        public double LastLoss => 0;
        public double AvgLoss100 => 0;
        public long TrainSteps => Replay.Count;
        public double AvgQ => 0;
        public Task<MlInferResult> InferAsync(float[] stateVec, float[] actionMask, MlConfig config, CancellationToken ct)
            => Task.FromResult(new MlInferResult(true, new double[ActionCatalog.Count], Enumerable.Repeat(1f / ActionCatalog.Count, ActionCatalog.Count).ToArray(), 0, 0));
        public Task<MlTrainResult> TrainAsync(Transition transition, MlConfig config, long tick, CancellationToken ct)
        { Replay.Add(transition); return Task.FromResult(new MlTrainResult(true, 0, 0, TrainSteps, false)); }
        public bool TryLoad(string path, out int episodeId)
        {
            episodeId = 0;
            if (!System.IO.File.Exists(path)) return false;
            var meta = JsonNode.Parse(System.IO.File.ReadAllText(path))!;
            episodeId = meta["episodeId"]!.GetValue<int>();
            Replay = JsonSerializer.Deserialize<List<Transition>>(System.IO.File.ReadAllText(meta["replayPath"]!.GetValue<string>()))!;
            return true;
        }
        public void TrySave(string path, int episodeId)
        {
            System.IO.File.WriteAllText(path + ".weights", "test weights");
            System.IO.File.WriteAllText(path + ".target", "test target");
            System.IO.File.WriteAllText(path + ".replay", JsonSerializer.Serialize(Replay));
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new { episodeId, weightsPath = path + ".weights", targetWeightsPath = path + ".target", replayPath = path + ".replay" }));
        }
        public void Reset(MlConfig config) => Replay.Clear();
        public void ResetCounters() { }
        public void UpdateConfig(MlConfig config) { }
        public bool TryConnect() => true;
        public void Disconnect() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
