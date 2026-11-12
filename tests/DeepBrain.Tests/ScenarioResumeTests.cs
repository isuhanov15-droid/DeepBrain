using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using Xunit;

namespace DeepBrain.Tests;

public sealed class ScenarioResumeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "deepbrain-scenario-" + Guid.NewGuid().ToString("N"));
    public ScenarioResumeTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);
    private PlanningConfig Settings => new() { Enable = true, Path = Path.Combine(_directory, "planning.json") };
    private ExecutivePlanner Planner() { var result = new ExecutivePlanner(); result.Configure(Settings); return result; }
    private BrainConfig Config => BrainConfig.Default with
    {
        ScenarioDefault = "fatigue_day", Planning = Settings,
        Ml = BrainConfig.Default.Ml with { Enable = false, Backend = "off" },
        Memory = MemoryConfig.Default with { Enable = false },
        Habits = HabitConfig.Default with { Enable = false, ImportPath = "" },
        Llm = LlmConfig.Default with { Enable = false, PersistJournal = false },
        Evaluation = BrainConfig.Default.Evaluation with { SaveReports = false }
    };
    private (LifeLoop Loop, List<LifeStateDto> States) Loop(BrainConfig config)
    {
        var path = Path.Combine(_directory, "config.json"); File.WriteAllText(path, JsonSerializer.Serialize(config));
        var states = new List<LifeStateDto>();
        return (new LifeLoop(new WorldSim(1337), new HomeostasisEngine(), new InstinctEngine(), new EmotionEngine(),
            new ActionSelector(), new Actuator(), new RewardEngine(), new LearningEngine(), new BrainConfigLoader(path, _ => { }),
            (s, _) => states.Add(s), (_, _) => { }, (_, _) => { }, _ => { }), states);
    }
    private static void Tick(LifeLoop loop) => typeof(LifeLoop).GetMethod("TickOnce", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(loop, new object[] { 0.2, CancellationToken.None });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualLifeLoopRestoresNonDefaultScenarioAndSameUnfinishedPlan(bool legacyFile)
    {
        var first = Loop(Config); Tick(first.Loop); first.Loop.FlushPersistentState();
        var before = Assert.Single(first.States); Assert.NotNull(before.ActivePlan?.PlanId);
        if (legacyFile)
        {
            var document = JsonNode.Parse(File.ReadAllText(Settings.Path))!;
            document.AsObject().Remove("scenarioContext"); File.WriteAllText(Settings.Path, document.ToJsonString());
        }
        var second = Loop(Config with { ScenarioDefault = "calm_baseline" }); Tick(second.Loop);
        var after = Assert.Single(second.States);
        Assert.Equal("fatigue_day", after.Scenario!.Name);
        Assert.Equal(before.ActivePlan!.PlanId, after.ActivePlan!.PlanId);
        Assert.True(after.ActivePlan.AgeTicks > before.ActivePlan.AgeTicks);
        Assert.Equal(before.Executive!.Interrupted, after.Executive!.Interrupted);
        Assert.True(after.Executive.RevalidatedAfterRestart);
        Assert.Equal("restored", after.Executive.ScenarioResumeStatus);
    }

    [Fact]
    public void ScenarioWithoutActivePlanSurvivesAndUsesNameInsteadOfStoredIndex()
    {
        var first = Planner(); first.RememberScenario("novelty_walk", 12);
        var scenarios = new Dictionary<string, ScenarioConfig>(BrainConfig.Default.Scenarios)
        { ["aaa_inserted"] = BrainConfig.Default.Scenarios["calm_baseline"] };
        var curriculum = new CurriculumManager(1337); curriculum.Configure(BrainConfig.Default.Curriculum, scenarios);
        var second = Planner(); Assert.Null(second.Snapshot().Plan);
        Assert.True(second.TryRestoreScenario(curriculum));
        Assert.Equal("novelty_walk", curriculum.ScenarioName);
        Assert.Equal(12, second.SavedScenario!.EpisodeId);
        Assert.Equal("novelty_walk", curriculum.ListScenarios()[curriculum.ScenarioIndex]);
    }

    [Fact]
    public void EpisodeBoundaryPersistsTheNextScenarioBeforeAnotherTickRuns()
    {
        var config = Config with { ScenarioDefault = "calm_baseline", Episode = Config.Episode with { MaxSteps = 2 } };
        var first = Loop(config); Tick(first.Loop); Tick(first.Loop); first.Loop.FlushPersistentState();
        var context = Planner().SavedScenario!;
        Assert.Equal("fatigue_day", context.Name); Assert.Equal(1, context.EpisodeId);
        var second = Loop(config); Tick(second.Loop);
        Assert.Equal(context.Name, second.States[0].Scenario!.Name);
        Assert.Equal(context.EpisodeId, second.States[0].Episode!.EpisodeId);
    }

    [Fact]
    public void ExplicitSelectionWhilePausedIsSavedImmediately()
    {
        var first = Loop(Config); Tick(first.Loop);
        Assert.True(first.Loop.TrySetScenario("social_pull"));
        Assert.Equal("social_pull", Planner().SavedScenario!.Name);
        Assert.False(first.Loop.TrySetScenario("not-a-real-scenario"));
        Assert.Equal("social_pull", Planner().SavedScenario!.Name);
    }

    [Fact]
    public void RemovedScenarioFallsBackAndRecordsWhyTheOldPlanCannotContinue()
    {
        var first = Loop(Config); Tick(first.Loop); first.Loop.FlushPersistentState();
        var oldPlan = first.States[0].ActivePlan!.PlanId;
        var scenarios = new Dictionary<string, ScenarioConfig>(Config.Scenarios); scenarios.Remove("fatigue_day");
        var second = Loop(Config with { ScenarioDefault = "calm_baseline", Scenarios = scenarios }); Tick(second.Loop);
        var state = Assert.Single(second.States);
        Assert.Equal("calm_baseline", state.Scenario!.Name);
        Assert.Equal("unavailable", state.Executive!.ScenarioResumeStatus);
        Assert.Contains(state.Executive.RecentOutcomes, o => o.PlanId == oldPlan && o.Reason == "context_changed");
    }

    [Theory]
    [InlineData("{\"name\":\"\",\"episodeId\":1}")]
    [InlineData("{\"name\":\"fatigue_day\",\"episodeId\":-1}")]
    [InlineData("{\"name\":\"fatigue_day\"}")]
    public void CorruptScenarioContextCannotOverwriteSavedHistory(string context)
    {
        Planner().RememberScenario("fatigue_day", 2);
        var document = JsonNode.Parse(File.ReadAllText(Settings.Path))!;
        document["scenarioContext"] = JsonNode.Parse(context);
        var invalid = document.ToJsonString(); File.WriteAllText(Settings.Path, invalid);
        var error = Record.Exception(() => Planner());
        Assert.True(error is JsonException or InvalidDataException, error?.ToString());
        Assert.Equal(invalid, File.ReadAllText(Settings.Path));
    }
}
