using System.Text.Json;
using System.Text.Json.Nodes;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using Xunit;

namespace DeepBrain.Tests;

public sealed class IntentionPlanningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deepbrain-intentions-" + Guid.NewGuid().ToString("N"));
    public IntentionPlanningTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);
    private PlanningConfig Config => new() { Enable = true, EnableStrategyLearning = true, EnableIntentions = true,
        Path = Path.Combine(_dir, "planning.json"), VerificationTicks = 1, CommitmentTicks = 1, StallTicks = 40 };
    private ExecutivePlanner Create(PlanningConfig? cfg = null) { var p = new ExecutivePlanner(); p.Configure(cfg ?? Config); return p; }
    private static GoalDto[] G(string id = "explore", double urgency = .5) => new[] { new GoalDto(id, id, urgency, .5, "test") };
    private static ExecutiveObservation O(double energy = .8, double safety = .9, double novelty = .5, bool social = false) =>
        new("novelty_walk", 1, new HomeostasisDto(energy, .2, .2, .1, safety), .5, novelty, social);
    private string ExploreOnce(ExecutivePlanner p)
    {
        var id = p.Begin(G(), O())!.PlanId!;
        p.Observe("explore_signal", O(), O(novelty: .4), false, 0);
        Assert.Equal(.5, p.Snapshot().Plan!.Progress);
        return id;
    }
    private static void Recover(ExecutivePlanner p)
    {
        Assert.Equal("restore_energy", p.Snapshot().Plan!.GoalId);
        Assert.Equal("succeeded", p.Observe("rest_short", O(.2), O(), false, 0)!.Status);
    }

    [Fact] public void RecoveryReturnsToSameIntentionProgressAndStrategyAttempt()
    {
        var p = Create(); var id = ExploreOnce(p); var attempt = p.Snapshot().StrategyLearning!.ActiveAttemptId;
        p.Begin(G(), O(.2)); var paused = p.Snapshot();
        Assert.Equal("restore_energy", paused.Plan!.GoalId);
        Assert.Equal(id, paused.Intentions!.MainPlanId); Assert.Equal(id, paused.Intentions.ParentPlanId);
        Assert.Equal(.5, Assert.Single(paused.Intentions.Deferred).Progress);
        Assert.Equal(0, paused.Interrupted); Assert.Equal(0, paused.StrategyLearning!.CompletedAttempts);
        Recover(p); Assert.Equal(id, p.Begin(G(), O())!.PlanId);
        Assert.Equal(.5, p.Snapshot().Plan!.Progress);
        Assert.Equal(attempt, p.Snapshot().StrategyLearning!.ActiveAttemptId);
        p.Observe("explore_signal", O(novelty: .4), O(novelty: .3), false, 0);
        var s = p.Snapshot(); Assert.Equal(2, s.Succeeded); Assert.Equal(0, s.Interrupted);
        Assert.Equal(1, s.Intentions!.Resumptions); Assert.Empty(s.Intentions.Deferred);
        Assert.Single(s.StrategyLearning!.RecentAttempts.Where(a => a.Id == attempt));
    }

    [Fact] public void TimeAndEnergySpentOnChildDoNotChargeParent()
    {
        var p = Create(Config with { StallTicks = 5 }); ExploreOnce(p);
        var age = p.Snapshot().Plan!.AgeTicks; p.Begin(G(), O(.2));
        for (var i = 0; i < 12; i++) { p.Begin(G(), O(.2)); p.Observe("rest_short", O(.2), O(.2), false, 0); }
        Recover(p); var resumed = p.Begin(G(), O())!;
        Assert.Equal(age, resumed.AgeTicks); Assert.Equal(0, resumed.Replans);
        var outcome = p.Observe("explore_signal", O(novelty: .4), O(novelty: .3), false, 0);
        Assert.Equal("succeeded", outcome!.Status);
        var history = p.Snapshot().StrategyLearning!.RecentAttempts.Single(a => a.GoalId == "explore");
        Assert.InRange(history.AwakeTicks, 1, 3); Assert.Equal(0, history.EnergySpent);
    }

    [Fact] public void RestartDuringChildKeepsBothPlansAndCountsEachAttemptOnce()
    {
        var p = Create(); var id = ExploreOnce(p); p.Begin(G(), O(.2)); p.Flush();
        var before = p.Snapshot(); p = Create(); var after = p.Snapshot();
        Assert.Equal(before.Plan!.PlanId, after.Plan!.PlanId);
        Assert.Equal(before.StrategyLearning!.ActiveAttemptId, after.StrategyLearning!.ActiveAttemptId);
        Assert.Equal(id, Assert.Single(after.Intentions!.Deferred).PlanId);
        p.Begin(G(), O(.2)); Recover(p); p.Flush();
        // Also restart in the valid checkpoint interval between child completion and parent resumption.
        p = Create(); Assert.Null(p.Snapshot().Plan); Assert.Equal(id, p.Begin(G(), O())!.PlanId);
        p.Observe("explore_signal", O(novelty: .4), O(novelty: .3), false, 0); p.Flush();
        var final = Create().Snapshot(); Assert.Equal(2, final.Succeeded);
        Assert.Equal(2, final.StrategyLearning!.CompletedAttempts); Assert.Equal(2, final.StrategyLearning.LearnedAttempts);
        Assert.Equal(1, final.Intentions!.Resumptions);
    }

    [Fact] public void ThreatCreatesNestedRecoveryThenUnwindsInOrder()
    {
        var p = Create(); var root = ExploreOnce(p); var child = p.Begin(G(), O(.2))!.PlanId;
        Assert.Equal("regulate", p.Begin(G(), O(.2, .1))!.GoalId);
        Assert.Equal(new[] { root, child }, p.Snapshot().Intentions!.Deferred.Select(d => d.PlanId));
        p.Observe("breathe_slow", O(.2, .1), O(.2), false, 0);
        Assert.Equal(child, p.Begin(G(), O(.2))!.PlanId); Recover(p);
        Assert.Equal(root, p.Begin(G(), O())!.PlanId);
        Assert.Equal(2, p.Snapshot().Intentions!.Resumptions);
    }

    [Fact] public void SleepFreezesWholeHierarchyAndRestartPreservesIt()
    {
        var p = Create(); var root = ExploreOnce(p); p.Begin(G(), O(.2)); var clock = p.Snapshot().AwakeClock;
        for (var i = 0; i < 100; i++) { p.Begin(G(), O() with { Sleeping = true }); p.Observe("rest_short", O(.2), O(), false, 0); }
        p.Flush(); p = Create(); var s = p.Snapshot();
        Assert.Equal(clock, s.AwakeClock); Assert.Equal("paused", s.Plan!.Status);
        Assert.Equal(root, Assert.Single(s.Intentions!.Deferred).PlanId); Assert.Equal(0, s.StrategyLearning!.CompletedAttempts);
        p.Begin(G(), O(.2)); Recover(p); Assert.Equal(root, p.Begin(G(), O())!.PlanId);
    }

    [Theory]
    [InlineData("novelty", "novelty_gone")]
    [InlineData("context", "context_changed")]
    [InlineData("episode", "context_changed")]
    public void ObsoleteIntentionsAreCancelledWithReasonWithoutTeachingFailure(string change, string reason)
    {
        var p = Create(); var root = ExploreOnce(p); p.Begin(G(), O(.2));
        var o = change switch { "novelty" => O(.2, novelty: 0), "context" => O(.2) with { Scenario = "fatigue_day" }, _ => O(.2) with { EpisodeId = 2 } };
        p.Begin(G(), o); var s = p.Snapshot(); Assert.Empty(s.Intentions!.Deferred);
        Assert.Contains(s.RecentOutcomes, x => x.PlanId == root && x.Status == "interrupted" && x.Reason == reason);
        Assert.Equal(0, s.StrategyLearning!.LearnedAttempts); p.Flush(); Create();
    }

    [Fact] public void DisappearedContactIsNotResumedOrCreditedAsSuccess()
    {
        var p = Create(); var id = p.Begin(G("connect"), O(social: true))!.PlanId;
        p.Begin(G("connect"), O(.2, social: true)); p.Begin(G(), O(.2));
        Assert.Contains(p.Snapshot().RecentOutcomes, x => x.PlanId == id && x.Reason == "contact_gone");
        Assert.Equal(0, p.Snapshot().Succeeded);
    }

    [Fact] public void DeferredExpiryDoesNotConsumeParentExecutionBudget()
    {
        var p = Create(Config with { MaxDeferredTicks = 20, StallTicks = 100 }); ExploreOnce(p); p.Begin(G(), O(.2));
        for (var i = 0; i < 22; i++) p.Begin(G(), O(.2));
        Assert.Empty(p.Snapshot().Intentions!.Deferred);
        Assert.Contains(p.Snapshot().RecentOutcomes, x => x.Reason == "deferred_expired" && x.AwakeTicks == 0);
        Assert.Equal(0, p.Snapshot().Failed);
    }

    [Fact] public void CapacityIsBoundedAndCancelsOldestWithExplanation()
    {
        var p = Create(Config with { MaxDeferredIntentions = 1 }); var root = ExploreOnce(p);
        var child = p.Begin(G(), O(.2))!.PlanId; p.Begin(G(), O(.2, .1));
        Assert.Equal(child, Assert.Single(p.Snapshot().Intentions!.Deferred).PlanId);
        Assert.Contains(p.Snapshot().RecentOutcomes, x => x.PlanId == root && x.Reason == "capacity_limit"); p.Flush(); Create(Config with { MaxDeferredIntentions = 1 });
    }

    [Fact] public void IncompleteSafetyCannotBePreemptedByCognitiveGoalOrDuplicated()
    {
        var p = Create(); ExploreOnce(p); var safety = p.Begin(G(), O(.2, .1))!.PlanId;
        for (var i = 0; i < 5; i++) Assert.Equal(safety, p.Begin(G("connect", 1), O(.2, .4, social: true))!.PlanId);
        Assert.Single(p.Snapshot().Intentions!.Deferred);
        var scores = new Dictionary<string, double> { ["breathe_slow"] = double.NegativeInfinity, ["focus_widen"] = .2 };
        p.ApplyBias(scores); Assert.True(double.IsNegativeInfinity(scores["breathe_slow"]));
    }

    [Fact] public void FailedPrerequisiteDoesNotImmediatelyResumeUnsafeParent()
    {
        var p = Create(Config with { StallTicks = 5, MaxReplans = 0 }); var root = ExploreOnce(p); p.Begin(G(), O(.2));
        for (var i = 0; i < 6; i++) { p.Begin(G(), O(.2)); p.Observe("rest_short", O(.2), O(.2), false, 0); }
        var next = p.Begin(G(), O(.2)); Assert.NotEqual(root, next?.PlanId);
        Assert.Contains(p.Snapshot().RecentOutcomes, x => x.PlanId == root && x.Reason == "prerequisite_unmet");
    }

    [Fact] public void LearningToggleClosesDeferredAttemptOnlyOnceAndPreservesIntention()
    {
        var p = Create(); var root = ExploreOnce(p); p.Begin(G(), O(.2));
        p.Configure(Config with { EnableStrategyLearning = false }); p.Flush();
        p = Create(Config with { EnableStrategyLearning = false }); Recover(p);
        p.Configure(Config); Assert.Equal(root, p.Begin(G(), O())!.PlanId);
        p.Observe("explore_signal", O(novelty: .4), O(novelty: .3), false, 0); p.Flush();
        var s = Create().Snapshot(); Assert.Equal(3, s.StrategyLearning!.CompletedAttempts); Assert.Equal(1, s.StrategyLearning.LearnedAttempts);
        Assert.Equal(3, s.StrategyLearning.RecentAttempts.Select(a => a.Id).Distinct().Count());
    }

    [Fact] public void DisablingIntentionsKeepsActiveChildAndClosesDeferredWithExplanation()
    {
        var p = Create(); ExploreOnce(p); p.Begin(G(), O(.2)); var child = p.Snapshot().Plan!.PlanId;
        p.Configure(Config with { EnableIntentions = false });
        Assert.Equal(child, p.Snapshot().Plan!.PlanId); Assert.Empty(p.Snapshot().Intentions!.Deferred);
        Assert.Contains(p.Snapshot().RecentOutcomes, x => x.Reason == "intentions_disabled");
        p = Create(); Assert.Empty(p.Snapshot().Intentions!.Deferred); Assert.Equal(child, p.Snapshot().Plan!.PlanId);
    }

    [Fact] public void MigrationPreservesLegacyAttemptAndCountersWithoutInventingHierarchy()
    {
        var p = Create(Config with { EnableIntentions = false }); var id = ExploreOnce(p); p.Flush();
        var doc = JsonNode.Parse(File.ReadAllText(Config.Path))!; doc.AsObject().Remove("intentions"); File.WriteAllText(Config.Path, doc.ToJsonString());
        p = Create(); Assert.Equal(id, p.Begin(G(), O())!.PlanId);
        Assert.Equal(0, p.Snapshot().Intentions!.Suspensions); Assert.Empty(p.Snapshot().Intentions!.Deferred);
        Assert.Equal(.5, p.Snapshot().Plan!.Progress);
    }

    [Fact] public void FailedSaveRetainsWholeHierarchyForRetry()
    {
        var p = Create(); var root = ExploreOnce(p); p.Flush();
        File.Delete(Config.Path); Directory.CreateDirectory(Config.Path);
        p.Begin(G(), O(.2)); Assert.NotNull(p.Snapshot().LastError);
        Assert.Equal(root, Assert.Single(p.Snapshot().Intentions!.Deferred).PlanId);
        Directory.Delete(Config.Path); p.Flush(); var restored = Create().Snapshot();
        Assert.Equal(root, Assert.Single(restored.Intentions!.Deferred).PlanId);
        Assert.Equal(p.Snapshot().Plan!.PlanId, restored.Plan!.PlanId); Assert.Null(p.Snapshot().LastError);
    }

    [Fact] public void ResumptionRequiresFreshRepeatedVerification()
    {
        var cfg = Config with { VerificationTicks = 3 }; var p = Create(cfg); var root = ExploreOnce(p);
        p.Begin(G(), O()); p.Observe("explore_signal", O(novelty:.4), O(novelty:.3), false, 0);
        Assert.Equal("verify", p.Snapshot().Plan!.Stage); p.Begin(G(), O(.2));
        for (var j=0;j<3;j++) { p.Begin(G(), O()); p.Observe("rest_short", O(.2), O(), false, 0); }
        Assert.Equal(root, p.Begin(G(), O())!.PlanId);
        Assert.Null(p.Observe("focus_widen", O(), O(), false, 0));
        p.Flush(); p=Create(cfg); p.Begin(G(), O());
        Assert.Null(p.Observe("focus_widen", O(), O(), false, 0));
        p.Begin(G(), O()); Assert.Null(p.Observe("focus_widen", O(), O(), false, 0));
        p.Begin(G(), O()); Assert.Equal("succeeded", p.Observe("focus_widen", O(), O(), false, 0)!.Status);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("missing")]
    [InlineData("clock")]
    [InlineData("attempt")]
    public void DamagedHierarchyIsRejectedWithoutOverwritingExperience(string corruption)
    {
        var p = Create(); ExploreOnce(p); p.Begin(G(), O(.2)); p.Flush();
        var doc = JsonNode.Parse(File.ReadAllText(Config.Path))!; var deferred = doc["intentions"]!["deferred"]![0]!;
        switch (corruption)
        {
            case "cycle": deferred["plan"]!["goalId"] = "restore_energy"; break;
            case "missing": deferred.AsObject().Remove("pausedAt"); break;
            case "clock": deferred["pausedAt"] = -1; break;
            case "attempt": deferred["plan"]!["attempt"]!["id"] = "wrong"; break;
        }
        var bad = doc.ToJsonString(); File.WriteAllText(Config.Path, bad);
        Assert.ThrowsAny<Exception>(() => Create()); Assert.Equal(bad, File.ReadAllText(Config.Path));
    }
}
