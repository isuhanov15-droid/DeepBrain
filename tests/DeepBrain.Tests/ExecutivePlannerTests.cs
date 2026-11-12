using System.Reflection;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using Xunit;

namespace DeepBrain.Tests;

public sealed class ExecutivePlannerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "deepbrain-planning-" + Guid.NewGuid().ToString("N"));
    public ExecutivePlannerTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);
    private PlanningConfig Settings => new() { Enable = true, Path = Path.Combine(_directory, "planning.json") };
    private ExecutivePlanner Create(PlanningConfig? config = null)
    { var planner = new ExecutivePlanner(); planner.Configure(config ?? Settings); return planner; }
    private static GoalDto G(string id, double urgency = 0.8) => new(id, "test", urgency, 0.5, "test");
    private static ExecutiveObservation O(double energy = 0.5, double fatigue = 0.4, double safety = 0.9,
        double pain = 0.1, double agency = 0.5, double novelty = 0.5, bool social = false) =>
        new("calm_baseline", 1, new HomeostasisDto(energy, fatigue, 0.1, pain, safety), agency, novelty, social);

    [Fact]
    public void OldConfigurationKeepsLegacyPlanningAndCreatesNoFile()
    {
        var planner = Create(Settings with { Enable = false });
        Assert.Null(planner.Begin(new[] { G("restore_energy") }, O()));
        planner.Flush();
        Assert.False(File.Exists(Settings.Path));
    }

    [Fact]
    public void GoalCommitmentResistsSmallUrgencyChangesButAllowsMeaningfulPreemption()
    {
        var planner = Create(Settings with { CommitmentTicks = 5 });
        var initial = planner.Begin(new[] { G("focus", 0.7), G("restore_energy", 0.65) }, O())!;
        for (var i = 0; i < 10; i++)
            Assert.Equal(initial.PlanId, planner.Begin(new[] { G("restore_energy", 0.8), G("focus", 0.7) }, O())!.PlanId);
        var changed = planner.Begin(new[] { G("restore_energy", 0.99), G("focus", 0.7) }, O());
        Assert.Equal("restore_energy", changed!.GoalId);
        Assert.Equal("higher_priority", Assert.Single(planner.Snapshot().RecentOutcomes).Reason);
    }

    [Fact]
    public void ThreatInterruptsExplorationImmediately()
    {
        var planner = Create();
        planner.Begin(new[] { G("explore") }, O(energy: 0.9));
        var recovery = planner.Begin(new[] { G("explore") }, O(energy: 0.9, safety: 0.2));
        Assert.Equal("regulate", recovery!.GoalId);
        Assert.Equal("breathe_slow", recovery.PreferredAction);
        Assert.Equal("urgent_recovery", Assert.Single(planner.Snapshot().RecentOutcomes).Reason);
    }

    [Fact]
    public void RecoveryCompletesOnlyAfterMeasuredTargetHolds()
    {
        var planner = Create(); var goal = new[] { G("restore_energy") }; var low = O();
        planner.Begin(goal, low);
        Assert.Null(planner.Observe("focus_widen", low, low, false, 0));
        Assert.Equal(0, planner.Snapshot().Succeeded);
        var recovered = O(energy: 0.8, fatigue: 0.2);
        for (var i = 0; i < 2; i++)
        {
            planner.Begin(goal, recovered);
            Assert.Null(planner.Observe("rest_short", low, recovered, false, 0));
            Assert.Equal("verify", planner.Snapshot().Plan!.Stage);
        }
        planner.Begin(goal, recovered);
        var result = planner.Observe("focus_widen", recovered, recovered, false, 0);
        Assert.Equal("succeeded", result!.Status); Assert.Equal(1, result.Progress);
        Assert.Null(planner.Snapshot().Plan);
    }

    [Fact]
    public void ExplorationRequiresMeasuredNoveltyReduction()
    {
        var planner = Create(Settings with { VerificationTicks = 1 }); var goals = new[] { G("explore") }; var start = O();
        planner.Begin(goals, start);
        Assert.Null(planner.Observe("explore_signal", start, start, false, 0));
        Assert.Equal(0, planner.Snapshot().Plan!.Progress);
        var first = start with { Novelty = 0.4 };
        planner.Begin(goals, start);
        Assert.Null(planner.Observe("explore_signal", start, first, true, 0));
        Assert.Equal(0, planner.Snapshot().Plan!.Progress);
        Assert.Null(planner.Observe("explore_signal", start, first, false, 0));
        Assert.Equal(0.5, planner.Snapshot().Plan!.Progress);
        planner.Begin(goals, first);
        Assert.Equal("succeeded", planner.Observe("explore_signal", first, first with { Novelty = 0.3 }, false, 0)!.Status);
    }

    [Fact]
    public void AlmostRecoveredDoesNotSatisfyThePublishedCriterion()
    {
        var planner = Create(Settings with { VerificationTicks = 1 }); var low = O();
        planner.Begin(new[] { G("restore_energy") }, low);
        Assert.Null(planner.Observe("rest_short", low, O(energy: 0.746, fatigue: 0.25), false, 0));
        Assert.Equal(0, planner.Snapshot().Succeeded);
    }

    [Fact]
    public void FailedSaveCannotDiscardHistoryWhenConfigurationChanges()
    {
        var planner = Create(); var plan = planner.Begin(new[] { G("explore") }, O())!;
        Directory.CreateDirectory(Settings.Path); // Prevent replacing this path with a file.
        planner.Flush(); Assert.NotNull(planner.Snapshot().LastError);
        Assert.Throws<IOException>(() => planner.Configure(Settings with { Path = Path.Combine(_directory, "other.json") }));
        Assert.Equal(plan.PlanId, planner.Snapshot().Plan!.PlanId);
        Assert.False(File.Exists(Path.Combine(_directory, "other.json")));
        Directory.Delete(Settings.Path);
        planner.Flush(); Assert.Null(planner.Snapshot().LastError);
        Assert.Equal(plan.PlanId, Create().Snapshot().Plan!.PlanId);
    }

    [Fact]
    public void SendingMessageWithoutHandledSignalIsNotSocialSuccess()
    {
        var planner = Create(Settings with { VerificationTicks = 1 }); var obs = O(social: true);
        planner.Begin(new[] { G("connect") }, obs);
        Assert.Null(planner.Observe("emit_message", obs, obs, false, 0));
        Assert.Equal(0, planner.Snapshot().Plan!.Progress);
        var outcome = planner.Observe("emit_message", obs, obs with { SocialSignalPending = false }, false, 1);
        Assert.Equal("succeeded", outcome!.Status);
    }

    [Fact]
    public void StalledPlanTriesAlternativeAndThenRecordsFailureWithoutImmediateRetry()
    {
        var planner = Create(Settings with { StallTicks = 5, MaxReplans = 1, MaxPlanTicks = 100 });
        var obs = O(); var goals = new[] { G("restore_energy") }; var sawAlternative = false;
        for (var i = 0; i < 15; i++)
        {
            var plan = planner.Begin(goals, obs);
            sawAlternative |= plan?.PreferredAction == "breathe_slow";
            planner.Observe("focus_widen", obs, obs, false, 0);
        }
        Assert.True(sawAlternative);
        var outcome = Assert.Single(planner.Snapshot().RecentOutcomes);
        Assert.Equal("failed", outcome.Status); Assert.Equal("no_progress", outcome.Reason);
        Assert.Equal(1, outcome.Replans); Assert.Null(planner.Snapshot().Plan);
    }

    [Fact]
    public void SleepPausesDeadlineAndRestoresSameIntentionOnWake()
    {
        var planner = Create(); var goals = new[] { G("restore_energy") }; var obs = O();
        var plan = planner.Begin(goals, obs)!; var clock = planner.Snapshot().AwakeClock;
        for (var i = 0; i < 1000; i++) planner.Begin(goals, obs with { Sleeping = true });
        Assert.Equal(clock, planner.Snapshot().AwakeClock);
        Assert.Equal("paused", planner.Snapshot().Plan!.Status);
        Assert.Equal(plan.PlanId, planner.Begin(goals, obs)!.PlanId);
        Assert.Equal("active", planner.Snapshot().Plan!.Status);
    }

    [Fact]
    public void PersistenceRestoresProgressAndRequiresFreshVerification()
    {
        var first = Create(); var goals = new[] { G("restore_energy") }; var obs = O();
        var plan = first.Begin(goals, obs)!; var recovered = O(energy: 0.8, fatigue: 0.2);
        for (var i = 0; i < 2; i++) { first.Begin(goals, recovered); first.Observe("rest_short", obs, recovered, false, 0); }
        first.Flush(); var clock = first.Snapshot().AwakeClock;
        var restored = Create(); Assert.Equal(plan.PlanId, restored.Snapshot().Plan!.PlanId);
        Assert.Equal(clock, restored.Snapshot().AwakeClock);
        restored.Begin(goals, recovered);
        Assert.Null(restored.Observe("focus_widen", recovered, recovered, false, 0));
        Assert.True(restored.Snapshot().RevalidatedAfterRestart);
        for (var i = 0; i < 2; i++) { restored.Begin(goals, recovered); restored.Observe("focus_widen", recovered, recovered, false, 0); }
        var again = Create(); Assert.Equal(1, again.Snapshot().Succeeded);
        Assert.Equal(plan.PlanId, Assert.Single(again.Snapshot().RecentOutcomes).PlanId);
    }

    [Fact]
    public void NewScenarioInvalidatesSavedIntentionWithoutInventingSuccess()
    {
        var first = Create(); first.Begin(new[] { G("explore") }, O()); first.Flush();
        var restored = Create(); restored.Begin(Array.Empty<GoalDto>(), O() with { Scenario = "fatigue_day", EpisodeId = 2 });
        var result = Assert.Single(restored.Snapshot().RecentOutcomes);
        Assert.Equal("interrupted", result.Status); Assert.Equal("context_changed", result.Reason);
        Assert.Equal(0, restored.Snapshot().Succeeded);
    }

    [Fact]
    public void CorruptStateFailsWithoutOverwritingExistingFile()
    {
        var planner = Create(); planner.Begin(new[] { G("explore") }, O()); planner.Flush();
        var corrupt = File.ReadAllText(Settings.Path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 123");
        File.WriteAllText(Settings.Path, corrupt);
        Assert.Throws<InvalidDataException>(() => Create());
        Assert.Equal(corrupt, File.ReadAllText(Settings.Path));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"awakeClock\":7}")]
    public void IncompleteFileIsNotTreatedAsEmptyHistory(string incomplete)
    {
        File.WriteAllText(Settings.Path, incomplete);
        Assert.Throws<JsonException>(() => Create());
        Assert.Equal(incomplete, File.ReadAllText(Settings.Path));
    }

    [Fact]
    public void MissingProgressFieldCannotSilentlyResetSavedIntention()
    {
        var planner = Create(); planner.Begin(new[] { G("explore") }, O()); planner.Flush();
        var state = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Settings.Path))!;
        state["active"]!.AsObject().Remove("explorations");
        var incomplete = state.ToJsonString(); File.WriteAllText(Settings.Path, incomplete);
        Assert.Throws<JsonException>(() => Create());
        Assert.Equal(incomplete, File.ReadAllText(Settings.Path));
    }

    [Fact]
    public void PlannerBiasIsBoundedAndCannotEnableMaskedAction()
    {
        var planner = Create(Settings with { MaxActionBias = 100 }); planner.Begin(new[] { G("restore_energy") }, O());
        var scores = new Dictionary<string, double> { ["rest_short"] = double.NegativeInfinity, ["focus_widen"] = 0.5 };
        planner.ApplyBias(scores); Assert.True(double.IsNegativeInfinity(scores["rest_short"]));
        Assert.Equal("unavailable", planner.Snapshot().BiasStatus);
        scores["rest_short"] = 0.3; planner.ApplyBias(scores);
        Assert.Equal(0.5, scores["rest_short"], 8); Assert.Equal(0.2, planner.Snapshot().AppliedBias);
    }

    [Fact]
    public void ActualLifeLoopPublishesMeasuredPlansAndPersistsThem()
    {
        var config = BrainConfig.Default with
        {
            Planning = Settings, Ml = BrainConfig.Default.Ml with { Enable = false, Backend = "off" },
            Memory = MemoryConfig.Default with { Enable = false }, Habits = HabitConfig.Default with { Enable = false, ImportPath = "" },
            Llm = LlmConfig.Default with { Enable = false, PersistJournal = false },
            Evaluation = BrainConfig.Default.Evaluation with { SaveReports = false }
        };
        var file = Path.Combine(_directory, "config.json"); File.WriteAllText(file, JsonSerializer.Serialize(config));
        var states = new List<LifeStateDto>();
        var loop = new LifeLoop(new WorldSim(1337), new HomeostasisEngine(), new InstinctEngine(), new EmotionEngine(),
            new ActionSelector(), new Actuator(), new RewardEngine(), new LearningEngine(), new BrainConfigLoader(file, _ => { }),
            (s, _) => states.Add(s), (_, _) => { }, (_, _) => { }, _ => { });
        var tick = typeof(LifeLoop).GetMethod("TickOnce", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < 1300; i++) tick.Invoke(loop, new object[] { 0.2, CancellationToken.None });
        loop.FlushPersistentState();
        Assert.Contains(states, s => s.Executive?.Plan?.PlanId is not null);
        Assert.Contains(states, s => s.Executive?.Succeeded > 0);
        Assert.Contains(states, s => s.Executive?.BiasStatus == "applied");
        Assert.Contains(states, s => s.Circadian?.IsSleeping == true);
        Assert.All(states.Where(s => s.ActivePlan is not null), s => Assert.Equal(s.Executive!.Plan!.PlanId, s.ActivePlan!.PlanId));
        Assert.True(File.Exists(Settings.Path));
        var restored = Create(); Assert.True(restored.Snapshot().Succeeded > 0);
    }
}
