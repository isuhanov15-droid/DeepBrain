using System.Reflection;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using Xunit;

namespace DeepBrain.Tests;

public sealed class IntentionSimulationTests
{
    [Fact] public void RealLifeLoopUsesBoundedPersistentIntentionsAcrossSixScenarios()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deepbrain-intention-sim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir); var scores = new List<Score>();
        try
        {
            foreach (var scenario in BrainConfig.Default.Scenarios.Keys.Order())
            foreach (var seed in new[] { 2207, 11003 })
            foreach (var enabled in new[] { false, true })
            {
                var folder = Path.Combine(dir, scenario + seed + enabled); Directory.CreateDirectory(folder);
                var planning = new PlanningConfig { Enable = true, EnableStrategyLearning = true, EnableIntentions = enabled,
                    Path = Path.Combine(folder, "planning.json"), SaveEveryTicks = 1000 };
                var config = BrainConfig.Default with { ScenarioDefault = scenario, CurriculumMode = "fixed",
                    Curriculum = BrainConfig.Default.Curriculum with { Mode = "fixed" }, Planning = planning,
                    CognitiveDynamics = new CognitiveDynamicsConfig { Enable = true, Path = Path.Combine(folder, "cognition.json"), SaveEveryTicks = 1000 },
                    Ml = BrainConfig.Default.Ml with { Enable = false, Backend = "off", Seed = seed },
                    Memory = MemoryConfig.Default with { Enable = false }, Habits = HabitConfig.Default with { Enable = false, ImportPath = "" },
                    Llm = LlmConfig.Default with { Enable = false, PersistJournal = false },
                    Evaluation = BrainConfig.Default.Evaluation with { SaveReports = false } };
                var file = Path.Combine(folder, "config.json"); File.WriteAllText(file, JsonSerializer.Serialize(config));
                LifeStateDto? latest = null; var unsafeTicks = 0; var resumedIds = new HashSet<string>();
                var suspendedIds = new HashSet<string>(); var completedResumptions = new HashSet<string>();
                var loop = new LifeLoop(new WorldSim(seed), new HomeostasisEngine(), new InstinctEngine(), new EmotionEngine(),
                    new ActionSelector(), new Actuator(), new RewardEngine(), new LearningEngine(), new BrainConfigLoader(file, _ => { }),
                    (s, _) =>
                    {
                        latest = s; var e = s.Executive!; var intentions = e.Intentions!;
                        if (s.Homeostasis.Safety < .35 || s.Homeostasis.Pain > .65) unsafeTicks++;
                        Assert.InRange(intentions.Deferred.Count, 0, planning.MaxDeferredIntentions);
                        Assert.Equal(intentions.Suspensions - intentions.Resumptions - intentions.Cancellations, intentions.Deferred.Count);
                        foreach (var h in intentions.RecentTransitions.Reverse())
                        {
                            if (h.Kind == "suspended") suspendedIds.Add(h.PlanId);
                            if (h.Kind == "resumed") { Assert.Contains(h.PlanId, suspendedIds); resumedIds.Add(h.PlanId); }
                        }
                        foreach (var r in e.RecentOutcomes)
                            if (r.Status == "succeeded" && resumedIds.Contains(r.PlanId)) completedResumptions.Add(r.PlanId);
                        Assert.Null(e.LastError); Assert.Null(s.Cognition!.LastError);
                    }, (_, _) => { }, (_, _) => { }, _ => { });
                var tick = typeof(LifeLoop).GetMethod("TickOnce", BindingFlags.NonPublic | BindingFlags.Instance)!;
                for (var i = 0; i < 4000; i++) tick.Invoke(loop, new object[] { .2, CancellationToken.None });
                loop.FlushPersistentState();
                var restored = new ExecutivePlanner(); restored.Configure(planning);
                var executive = latest!.Executive!; var stored = restored.Snapshot();
                Assert.Equal(executive.Intentions!.Resumptions, stored.Intentions!.Resumptions);
                Assert.Equal(executive.StrategyLearning!.CompletedAttempts, stored.StrategyLearning!.CompletedAttempts);
                Assert.Equal(executive.Plan?.PlanId, stored.Plan?.PlanId);
                Assert.Equal(executive.Intentions.Deferred.Select(d => d.PlanId), stored.Intentions.Deferred.Select(d => d.PlanId));
                scores.Add(new(scenario, seed, enabled, executive.Succeeded, executive.Failed, executive.Interrupted, unsafeTicks,
                    executive.Intentions.Suspensions, executive.Intentions.Resumptions, executive.Intentions.Cancellations, completedResumptions.Count));
            }
            var output = Environment.GetEnvironmentVariable("DEEPBRAIN_INTENTIONS_SIMULATION_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output)) File.WriteAllText(output, JsonSerializer.Serialize(scores, new JsonSerializerOptions { WriteIndented = true }));
            Assert.Equal(24, scores.Count);
            Assert.True(scores.Where(s => s.Enabled).Sum(s => s.Resumptions) > 0);
            Assert.True(scores.Sum(s => s.CompletedAfterResumption) > 0);
            Assert.True(scores.Where(s => s.Enabled).Sum(s => s.UnsafeTicks) <= scores.Where(s => !s.Enabled).Sum(s => s.UnsafeTicks), "Safety regression");
        }
        finally { Directory.Delete(dir, true); }
    }
    private sealed record Score(string Scenario, int Seed, bool Enabled, long Succeeded, long Failed, long Interrupted,
        int UnsafeTicks, long Suspensions, long Resumptions, long Cancellations, int CompletedAfterResumption);
}
