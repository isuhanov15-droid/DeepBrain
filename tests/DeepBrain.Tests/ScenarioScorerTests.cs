using System;
using System.Collections.Generic;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class ScenarioScorerTests
{
    [Fact]
    public void CalmBaselinePassesWhenLowAnxiousAndLoop()
    {
        var scorer = new ScenarioScorer();
        var report = BuildReport(
            scenario: "calm_baseline",
            steps: 100,
            loopCount: 0,
            moods: new Dictionary<string, int> { ["calm"] = 90, ["anxious"] = 5 },
            reward: new RewardDto(0.01, 0, 0, 0, 0, 0.01)
        );
        var score = scorer.Score("calm_baseline", report);
        Assert.True(score.Passed);
    }

    [Fact]
    public void NoveltyWalkFailsWhenNoExplore()
    {
        var scorer = new ScenarioScorer();
        var report = BuildReport(
            scenario: "novelty_walk",
            steps: 100,
            loopCount: 0,
            moods: new Dictionary<string, int> { ["curious"] = 40 },
            reward: new RewardDto(0, 0, 0, 0, 0, 0)
        );
        var score = scorer.Score("novelty_walk", report);
        Assert.False(score.Passed);
    }

    private static EpisodeReport BuildReport(string scenario, int steps, int loopCount, Dictionary<string, int> moods, RewardDto reward)
    {
        return new EpisodeReport(
            EpisodeId: 1,
            ScenarioName: scenario,
            StartTs: DateTimeOffset.UtcNow.AddSeconds(-10),
            EndTs: DateTimeOffset.UtcNow,
            Steps: steps,
            EndReason: "timeout",
            AvgReward: reward.Total,
            TotalReward: reward.Total * steps,
            RewardBreakdownAvg: reward,
            ActionHistogram: new Dictionary<string, int> { ["rest_short"] = steps },
            LoopCount: loopCount,
            MaxLoopStrength: 0.1,
            MoodDistribution: moods,
            AvgPain: 0.1,
            MaxPain: 0.2,
            AvgSafety: 0.8,
            AvgArousal: 0.2,
            SocialSignalsSent: 0,
            SelfTalkCount: 0,
            MaskFallbackCount: 0,
            InvalidActionCount: 0,
            MlUsed: false,
            BackendKind: "stub",
            EpsilonUsed: 0.0,
            ScenarioScore: null
        );
    }
}
