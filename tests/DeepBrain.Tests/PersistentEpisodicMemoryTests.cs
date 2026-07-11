using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.BrainDtos.V6;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DeepBrain.Tests;

public sealed class PersistentEpisodicMemoryTests
{
    [Fact]
    public void ConfigurationWithoutMemorySectionUsesSafeFallback()
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(BrainConfig.Default))!.AsObject();
        Assert.True(node.Remove(nameof(BrainConfig.Memory)));

        var config = JsonSerializer.Deserialize<BrainConfig>(node.ToJsonString(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        var memory = config!.Memory ?? MemoryConfig.Default;

        Assert.True(memory.Enable);
        Assert.Equal("memory/episodes.jsonl", memory.Path);
    }

    [Fact]
    public void PersistsAndReloadsCompletedEpisodes()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "episodes.jsonl");
            var config = BuildConfig(path, minEpisodesBeforeBias: 1, minEpisodeSteps: 1);
            var first = new PersistentEpisodicMemory(_ => { });
            first.Configure(config);

            var stored = first.Remember(BuildReport(
                episodeId: 7,
                scenario: "calm_baseline",
                avgReward: 0.02,
                actions: new Dictionary<string, int> { ["focus_widen"] = 80, ["rest_short"] = 20 },
                passed: true));

            Assert.NotNull(stored);
            Assert.True(File.Exists(path));

            var reloaded = new PersistentEpisodicMemory(_ => { });
            reloaded.Configure(config);
            var recent = reloaded.GetRecent(5);

            Assert.Single(recent);
            Assert.Equal(7, recent[0].EpisodeId);
            Assert.Equal("calm_baseline", recent[0].ScenarioName);
            Assert.Equal(80, recent[0].ActionHistogram["focus_widen"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RecallPrefersSimilarEpisodeAndProducesBoundedPositiveBias()
    {
        var dir = CreateTempDirectory();
        try
        {
            var memory = new PersistentEpisodicMemory(_ => { });
            memory.Configure(BuildConfig(Path.Combine(dir, "episodes.jsonl"), 1, 1));
            memory.Remember(BuildReport(
                episodeId: 10,
                scenario: "calm_baseline",
                avgReward: 0.03,
                actions: new Dictionary<string, int> { ["focus_widen"] = 90, ["rest_short"] = 10 },
                passed: true,
                mood: "calm",
                avgPain: 0.1,
                avgSafety: 0.9,
                avgArousal: 0.2));
            memory.Remember(BuildReport(
                episodeId: 11,
                scenario: "threat_pulses",
                avgReward: -0.03,
                actions: new Dictionary<string, int> { ["focus_narrow"] = 100 },
                passed: false,
                mood: "anxious",
                avgPain: 0.8,
                avgSafety: 0.2,
                avgArousal: 0.9));

            var result = memory.BuildActionBias(new MemoryCue("calm_baseline", "calm", 0.1, 0.9, 0.2));

            Assert.Equal<int?>(10, result.BestEpisodeId);
            Assert.True(result.BestSimilarity > 0.99);
            Assert.True(result.ActionBiases["focus_widen"] > 0);
            Assert.InRange(result.ActionBiases["focus_widen"], 0, 0.15);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FailedLoopEpisodePenalizesItsRepeatedAction()
    {
        var dir = CreateTempDirectory();
        try
        {
            var memory = new PersistentEpisodicMemory(_ => { });
            memory.Configure(BuildConfig(Path.Combine(dir, "episodes.jsonl"), 1, 1));
            memory.Remember(BuildReport(
                episodeId: 12,
                scenario: "mixed_adaptive",
                avgReward: -0.02,
                actions: new Dictionary<string, int> { ["focus_narrow"] = 100 },
                passed: false,
                mood: "neutral",
                loopCount: 1));

            var result = memory.BuildActionBias(new MemoryCue("mixed_adaptive", "neutral", 0.2, 0.7, 0.3));

            Assert.True(result.ActionBiases["focus_narrow"] < 0);
            Assert.InRange(result.ActionBiases["focus_narrow"], -0.15, 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PositiveRewardCannotReinforceFailedScenarioActions()
    {
        var dir = CreateTempDirectory();
        try
        {
            var memory = new PersistentEpisodicMemory(_ => { });
            memory.Configure(BuildConfig(Path.Combine(dir, "episodes.jsonl"), 1, 1));
            memory.Remember(BuildReport(
                episodeId: 14,
                scenario: "social_pull",
                avgReward: 0.02,
                actions: new Dictionary<string, int> { ["focus_widen"] = 100 },
                passed: false));

            var result = memory.BuildActionBias(new MemoryCue("social_pull", "calm", 0.2, 0.7, 0.3));

            Assert.True(result.ActionBiases["focus_widen"] < 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SkipsMalformedLinesAndEpisodesThatAreTooShort()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "episodes.jsonl");
            File.WriteAllText(path, "not-json" + Environment.NewLine);
            var memory = new PersistentEpisodicMemory(_ => { });
            memory.Configure(BuildConfig(path, minEpisodesBeforeBias: 1, minEpisodeSteps: 25));

            var stored = memory.Remember(BuildReport(
                episodeId: 13,
                scenario: "calm_baseline",
                avgReward: 0.02,
                actions: new Dictionary<string, int> { ["rest_short"] = 10 },
                passed: true,
                steps: 10));
            var status = memory.GetStatus();

            Assert.Null(stored);
            Assert.Equal(0, status.EpisodeCount);
            Assert.Equal(1, status.InvalidLines);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static MemoryConfig BuildConfig(
        string path,
        int minEpisodesBeforeBias,
        int minEpisodeSteps)
    {
        return new MemoryConfig(
            Enable: true,
            Path: path,
            MaxEpisodes: 100,
            RecallTopK: 8,
            MinSimilarity: 0.45,
            MaxActionBias: 0.15,
            RecencyHalfLifeDays: 90,
            MinEpisodesBeforeBias: minEpisodesBeforeBias,
            MinEpisodeSteps: minEpisodeSteps,
            RewardScale: 0.02
        );
    }

    private static EpisodeReport BuildReport(
        int episodeId,
        string scenario,
        double avgReward,
        Dictionary<string, int> actions,
        bool passed,
        string mood = "calm",
        double avgPain = 0.2,
        double avgSafety = 0.7,
        double avgArousal = 0.3,
        int loopCount = 0,
        int steps = 100)
    {
        return new EpisodeReport(
            EpisodeId: episodeId,
            ScenarioName: scenario,
            StartTs: DateTimeOffset.UtcNow.AddMinutes(-1),
            EndTs: DateTimeOffset.UtcNow,
            Steps: steps,
            EndReason: loopCount > 0 ? "loop" : "timeout",
            AvgReward: avgReward,
            TotalReward: avgReward * steps,
            RewardBreakdownAvg: new RewardDto(avgReward, 0, 0, 0, 0, avgReward),
            ActionHistogram: actions,
            LoopCount: loopCount,
            MaxLoopStrength: loopCount > 0 ? 1.0 : 0.0,
            MoodDistribution: new Dictionary<string, int> { [mood] = steps },
            AvgPain: avgPain,
            MaxPain: avgPain,
            AvgSafety: avgSafety,
            AvgArousal: avgArousal,
            SocialSignalsSent: 0,
            SelfTalkCount: 0,
            MaskFallbackCount: 0,
            InvalidActionCount: 0,
            MlUsed: true,
            BackendKind: "remote",
            EpsilonUsed: 0.05,
            ScenarioScore: new ScenarioScore(passed, passed ? "ok" : "failed")
        );
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "deepbrain-memory-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
