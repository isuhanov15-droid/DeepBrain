using System;
using System.Collections.Generic;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class EpisodeReportTests
{
    [Fact]
    public void EpisodeReportSerializesWithRequiredFields()
    {
        var report = new EpisodeReport(
            EpisodeId: 2,
            ScenarioName: "calm_baseline",
            StartTs: DateTimeOffset.UtcNow.AddSeconds(-5),
            EndTs: DateTimeOffset.UtcNow,
            Steps: 120,
            EndReason: "timeout",
            AvgReward: 0.01,
            TotalReward: 1.2,
            RewardBreakdownAvg: new RewardDto(0.01, 0, 0, 0, 0, 0.01),
            ActionHistogram: new Dictionary<string, int> { ["rest_short"] = 120 },
            LoopCount: 0,
            MaxLoopStrength: 0.0,
            MoodDistribution: new Dictionary<string, int> { ["calm"] = 120 },
            AvgPain: 0.2,
            MaxPain: 0.3,
            AvgSafety: 0.8,
            AvgArousal: 0.2,
            SocialSignalsSent: 0,
            SelfTalkCount: 0,
            MaskFallbackCount: 0,
            InvalidActionCount: 0,
            MlUsed: false,
            BackendKind: "stub",
            EpsilonUsed: 0.0,
            ScenarioScore: new ScenarioScore(true, "ok")
        )
        {
            ReportSchemaVersion = 3,
            RunId = "run-test",
            HostVersion = "1.4.0",
            ConfigVersion = "cfg-test"
        };

        var json = JsonSerializer.Serialize(report);
        var roundTrip = JsonSerializer.Deserialize<EpisodeReport>(json);
        Assert.NotNull(roundTrip);
        Assert.Equal(report.EpisodeId, roundTrip!.EpisodeId);
        Assert.Equal(report.ScenarioName, roundTrip.ScenarioName);
        Assert.Equal(report.Steps, roundTrip.Steps);
        Assert.Equal(report.EndReason, roundTrip.EndReason);
        Assert.Equal(3, roundTrip.ReportSchemaVersion);
        Assert.Equal("run-test", roundTrip.RunId);
        Assert.Equal("1.4.0", roundTrip.HostVersion);
        Assert.Equal("cfg-test", roundTrip.ConfigVersion);
    }
}
