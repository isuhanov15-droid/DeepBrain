using System.Text.Json;
using System.Text.Json.Nodes;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using Xunit;

namespace DeepBrain.Tests;

public sealed class HabitSystemTests
{
    [Fact]
    public void ConfigurationWithoutHabitsSectionUsesSafeFallback()
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(BrainConfig.Default))!.AsObject();
        Assert.True(node.Remove(nameof(BrainConfig.Habits)));

        var config = JsonSerializer.Deserialize<BrainConfig>(node.ToJsonString(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        var habits = config!.Habits ?? HabitConfig.Default;

        Assert.True(habits.Enable);
        Assert.Equal("memory/habits.v2.json", habits.Path);
        Assert.Equal(0.20, habits.CriticalSkillFloor, 3);
    }

    [Fact]
    public void NonRoutineOutcomeUpdatesCueBaselineButNotHabitStrength()
    {
        var habits = new HabitSystem();
        var before = Assert.IsType<DeepBrain.Shared.BrainDtos.V5.HabitDto>(
            habits.GetHabit("rest_recover"));

        var after = Assert.IsType<DeepBrain.Shared.BrainDtos.V5.HabitDto>(
            habits.UpdateAfter("focus_widen", 0.02, "fatigue_high", 10));

        Assert.Equal(before.Strength, after.Strength, 10);
        Assert.Equal(1L, after.CueExposures);
        Assert.Equal(0L, after.RoutineExecutions);
        Assert.Equal(0, after.Uses);
        Assert.Equal(0.02, after.CueRewardBaseline, 6);
    }

    [Fact]
    public void RoutineStrengthUsesAdvantageOverCueBaseline()
    {
        var habits = new HabitSystem();
        habits.UpdateAfter("focus_widen", 0.01, "fatigue_high", 1);

        var reinforced = Assert.IsType<DeepBrain.Shared.BrainDtos.V5.HabitDto>(
            habits.UpdateAfter("rest_short", 0.05, "fatigue_high", 2));

        Assert.True(reinforced.Strength > 0.5);
        Assert.Equal(2L, reinforced.CueExposures);
        Assert.Equal(1L, reinforced.RoutineExecutions);
        Assert.Equal(1L, reinforced.PositiveReinforcements);
        Assert.Equal(0L, reinforced.NegativeReinforcements);
        Assert.True(reinforced.AdvantageAverage > 0);
    }

    [Fact]
    public void LegacySnapshotMigratesExposureWithoutInventingExecutions()
    {
        using var temp = new TempDirectory();
        var importPath = Path.Combine(temp.Path, "legacy.json");
        var storePath = Path.Combine(temp.Path, "habits.v2.json");
        File.WriteAllText(importPath, """
        {
          "character": {
            "habitsTop": [
              {
                "id": "rest_recover",
                "cueKey": "fatigue_high",
                "routineAction": "rest_short",
                "strength": 0.9776,
                "uses": 60864,
                "avgReward": 0.01978
              },
              {
                "id": "regulate_breathe",
                "cueKey": "threat_high",
                "routineAction": "breathe_slow",
                "strength": 0,
                "uses": 147753,
                "avgReward": 0.01615
              }
            ]
          }
        }
        """);

        var habits = new HabitSystem();
        habits.Configure(new HabitConfig
        {
            Path = storePath,
            ImportPath = importPath,
            CriticalSkillFloor = 0.20,
            MaxStrength = 0.95
        });

        var rest = Assert.IsType<DeepBrain.Shared.BrainDtos.V5.HabitDto>(
            habits.GetHabit("rest_recover"));
        var breathe = Assert.IsType<DeepBrain.Shared.BrainDtos.V5.HabitDto>(
            habits.GetHabit("regulate_breathe"));
        Assert.Equal(0.95, rest.Strength, 6);
        Assert.Equal(60864L, rest.CueExposures);
        Assert.Equal(0L, rest.RoutineExecutions);
        Assert.True(rest.ImportedLegacy);
        Assert.Equal(0.20, breathe.Strength, 6);
        Assert.Equal(147753L, breathe.CueExposures);
        Assert.Equal(0L, breathe.RoutineExecutions);
        Assert.True(File.Exists(storePath));

        using var document = JsonDocument.Parse(File.ReadAllText(storePath));
        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void PersistedStateSurvivesReload()
    {
        using var temp = new TempDirectory();
        var storePath = Path.Combine(temp.Path, "habits.v2.json");
        var config = new HabitConfig
        {
            Path = storePath,
            ImportPath = "",
            SaveEveryUpdates = 100
        };
        var first = new HabitSystem();
        first.Configure(config);
        first.UpdateAfter("focus_widen", 0.01, "fatigue_high", 1);
        first.UpdateAfter("rest_short", 0.05, "fatigue_high", 2);
        first.Flush();

        var second = new HabitSystem();
        second.Configure(config);
        var restored = Assert.IsType<DeepBrain.Shared.BrainDtos.V5.HabitDto>(
            second.GetHabit("rest_recover"));

        Assert.Equal(2L, restored.CueExposures);
        Assert.Equal(1L, restored.RoutineExecutions);
        Assert.Equal(1L, restored.PositiveReinforcements);
        Assert.True(restored.AdvantageAverage > 0);
        Assert.True(second.GetStatus().Loaded);
    }

    [Fact]
    public void InvalidStoreIsBackedUpAndReplacedWithSafeSchema()
    {
        using var temp = new TempDirectory();
        var storePath = Path.Combine(temp.Path, "habits.v2.json");
        File.WriteAllText(storePath, "not-json");

        var habits = new HabitSystem();
        habits.Configure(new HabitConfig
        {
            Path = storePath,
            ImportPath = ""
        });

        using var document = JsonDocument.Parse(File.ReadAllText(storePath));
        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Single(Directory.GetFiles(temp.Path, "habits.v2.json.invalid-*"));
        Assert.True(habits.GetStatus().Loaded);
    }

    [Fact]
    public void ModerateFatigueDoesNotHideSocialCueButExhaustionDoes()
    {
        var habits = new HabitSystem();
        var attention = new AttentionDto("social", null, 0.7, "social_ping", "social", 0);
        var circadian = new CircadianDto("day", 0.5, false, 0.2);
        var events = new[]
        {
            new WorldEventDto(1, "social_ping", 0.5, "test", 0, 0.5, false)
        };

        var moderate = habits.ComputeCue(
            attention,
            circadian,
            new LoopDetector(),
            events,
            new HomeostasisDto(0.5, 0.65, 0.2, 0.1, 0.8));
        var exhausted = habits.ComputeCue(
            attention,
            circadian,
            new LoopDetector(),
            events,
            new HomeostasisDto(0.2, 0.90, 0.2, 0.1, 0.8));

        Assert.Equal("evening_social_ping", moderate);
        Assert.Equal("fatigue_high", exhausted);
    }

    [Fact]
    public void InactivitySurvivesMultipleRestartsWithoutCueUpdates()
    {
        using var temp = new TempDirectory();
        var config = new HabitConfig { Path = Path.Combine(temp.Path, "clock.json"), ImportPath = "",
            DecayEveryTicks = 100, InactiveDecayAfterTicks = 1000, InactiveDecayRate = 0.1 };
        var first = new HabitSystem();
        first.Configure(config);
        first.UpdateAfter("rest_short", 0.01, "fatigue_high", 10000);
        first.AdvanceTime(10400);
        first.Flush();
        var second = new HabitSystem();
        second.Configure(config);
        Assert.Equal(10400L, second.GetStatus().ClockTick);
        second.AdvanceTime(400);
        second.Flush(); // No reward updates; elapsed time must still be saved.
        var third = new HabitSystem();
        third.Configure(config);
        Assert.Empty(third.ApplyDecay(100).Where(c => c.after.Id == "rest_recover"));
        third.Configure(config); // Normal per-tick Configure must not reset time.
        var changes = third.ApplyDecay(300);
        Assert.Contains(changes, c => c.after.Id == "rest_recover" && c.after.Strength < c.before.Strength);
        Assert.Equal(11100L, third.GetStatus().ClockTick);
        Assert.Empty(third.ApplyDecay(300)); // No duplicate decay at the same tick.
    }

    [Fact]
    public void V2MigrationPreservesEvidenceButDoesNotTrustOldSessionTicks()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "legacy-v2.json");
        var state = new HabitPersistentState("rest_recover", "fatigue_high", "rest_short",
            0.7, 100, 60, 40, 20, 0.01, 0.03, 0.02, 5754253, 30807, true);
        File.WriteAllText(path, JsonSerializer.Serialize(new HabitPersistenceSnapshot(2, DateTimeOffset.UtcNow, new[] { state })));
        var habits = new HabitSystem();
        habits.Configure(new HabitConfig { Path = path, ImportPath = "" });
        var restored = habits.GetHabit("rest_recover")!;
        Assert.Equal(60L, restored.RoutineExecutions);
        Assert.Equal(0.7, restored.Strength, 8);
        Assert.Equal(0L, restored.LastCueTick);
        Assert.Equal(0L, restored.LastExecutionTick);
        Assert.Contains(habits.ApplyDecay(7000), c => c.after.Id == "rest_recover");
    }

    [Fact]
    public void NewCueAfterRestartUsesSameClockAsDecay()
    {
        using var temp = new TempDirectory();
        var config = new HabitConfig { Path = Path.Combine(temp.Path, "clock.json"), ImportPath = "" };
        var first = new HabitSystem(); first.Configure(config);
        first.AdvanceTime(100000); first.Flush();
        var second = new HabitSystem(); second.Configure(config);
        var updated = second.UpdateAfter("rest_short", 0.01, "fatigue_high", 10)!;
        Assert.Equal(100010L, updated.LastCueTick);
        Assert.Empty(second.ApplyDecay(1000).Where(c => c.after.Id == "rest_recover"));
        Assert.Contains(second.ApplyDecay(7000), c => c.after.Id == "rest_recover");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "deepbrain-habits-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
