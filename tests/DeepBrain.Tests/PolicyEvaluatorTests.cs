using System;
using System.Collections.Generic;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class PolicyEvaluatorTests
{
    [Fact]
    public void AggregatesMetricsOverWindow()
    {
        var evaluator = new PolicyEvaluator(new ScenarioScorer());
        evaluator.Configure(new EvaluationConfig(10, true));

        var report1 = BuildReport(steps: 100, totalReward: 1.0, loopCount: 0, moods: new Dictionary<string, int> { ["calm"] = 80 });
        var report2 = BuildReport(steps: 100, totalReward: -0.5, loopCount: 1, moods: new Dictionary<string, int> { ["anxious"] = 60 });

        evaluator.Add(report1, isEvaluation: true);
        evaluator.Add(report2, isEvaluation: true);

        var snapshot = evaluator.Snapshot(isEvaluation: true);
        Assert.Equal(2, snapshot.EpisodeCount);
        Assert.True(snapshot.LoopRate > 0);
        Assert.True(snapshot.MeanReward < 1.0);
    }

    private static EpisodeReport BuildReport(int steps, double totalReward, int loopCount, Dictionary<string, int> moods)
    {
        return new EpisodeReport(
            EpisodeId: 1,
            ScenarioName: "calm_baseline",
            StartTs: DateTimeOffset.UtcNow.AddSeconds(-10),
            EndTs: DateTimeOffset.UtcNow,
            Steps: steps,
            EndReason: "timeout",
            AvgReward: totalReward / steps,
            TotalReward: totalReward,
            RewardBreakdownAvg: new RewardDto(0, 0, 0, 0, 0, totalReward / steps),
            ActionHistogram: new Dictionary<string, int> { ["rest_short"] = steps },
            LoopCount: loopCount,
            MaxLoopStrength: 0.4,
            MoodDistribution: moods,
            AvgPain: 0.3,
            MaxPain: 0.5,
            AvgSafety: 0.7,
            AvgArousal: 0.3,
            SocialSignalsSent: 0,
            SelfTalkCount: 0,
            MaskFallbackCount: 0,
            InvalidActionCount: 0,
            MlUsed: false,
            BackendKind: "stub",
            EpsilonUsed: 0.0,
            ScenarioScore: new ScenarioScore(true, "ok")
        );
    }
}
