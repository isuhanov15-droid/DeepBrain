using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class CognitiveMemoryTests
{
    [Fact]
    public void VersionOneMemoryConfigurationGetsSafeV2Defaults()
    {
        const string json =
            "{\"enable\":true,\"path\":\"memory/episodes.jsonl\"," +
            "\"maxEpisodes\":5000,\"recallTopK\":8,\"minSimilarity\":0.45," +
            "\"maxActionBias\":0.15,\"recencyHalfLifeDays\":90," +
            "\"minEpisodesBeforeBias\":3,\"minEpisodeSteps\":25,\"rewardScale\":0.02}";

        var config = JsonSerializer.Deserialize<MemoryConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(config);
        Assert.Equal(0.96, config!.DeduplicationSimilarity, 6);
        Assert.Equal(50, config.ConsolidateEveryEpisodes);
        Assert.Equal(512, config.MaxIndexedCandidates);
    }

    [Fact]
    public void DuplicateEpisodesAreReinforcedInsteadOfAppended()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "episodes.jsonl");
            var memory = CreateMemory(path);

            memory.Remember(BuildReport(1, "calm_baseline", 0.02));
            memory.Remember(BuildReport(2, "calm_baseline", 0.02));

            var status = memory.GetStatus();
            var entry = Assert.Single(memory.GetRecent(5));
            Assert.Equal(1, status.EpisodeCount);
            Assert.Equal(2, status.RepresentedEpisodes);
            Assert.Equal(2, entry.Occurrences);
            Assert.True(entry.Strength > 0);
            Assert.Equal(1, status.DuplicateMerges);
            Assert.True(File.Exists(Path.Combine(dir, "experience.json")));

            var reloaded = CreateMemory(path);
            Assert.Equal(2, Assert.Single(reloaded.GetRecent(5)).Occurrences);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConsolidationCombinesSimilarNonDuplicateEpisodes()
    {
        var dir = CreateTempDirectory();
        try
        {
            var config = BuildConfig(Path.Combine(dir, "episodes.jsonl")) with
            {
                DeduplicationSimilarity = 0.9999,
                ConsolidationSimilarity = 0.90
            };
            var memory = new PersistentEpisodicMemory(_ => { });
            memory.Configure(config);
            memory.Remember(BuildReport(1, "novelty_walk", 0.02, avgPain: 0.20));
            memory.Remember(BuildReport(2, "novelty_walk", 0.02, avgPain: 0.22));

            Assert.Equal(2, memory.GetStatus().EpisodeCount);
            var result = memory.Consolidate();

            Assert.Equal(2, result.EntriesBefore);
            Assert.Equal(1, result.EntriesAfter);
            Assert.Equal(1, result.MergedEntries);
            Assert.Equal(2, result.RepresentedEpisodes);
            Assert.Equal(2, Assert.Single(memory.GetRecent(5)).Occurrences);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConsolidationForgetsOldWeakMemoryButProtectsCriticalExperience()
    {
        var dir = CreateTempDirectory();
        try
        {
            var config = BuildConfig(Path.Combine(dir, "episodes.jsonl")) with
            {
                ForgetAfterDays = 30,
                ForgetThreshold = 0.20,
                DeduplicationSimilarity = 1.0
            };
            var memory = new PersistentEpisodicMemory(_ => { });
            memory.Configure(config);
            var old = DateTimeOffset.UtcNow.AddDays(-400);
            memory.Remember(BuildReport(1, "weak_old", 0, endTs: old));
            memory.Remember(BuildReport(2, "critical_old", -0.02, passed: false, endReason: "panic", endTs: old));

            var result = memory.Consolidate();
            var remaining = Assert.Single(memory.GetRecent(10));

            Assert.Equal(1, result.ForgottenEntries);
            Assert.Equal("critical_old", remaining.ScenarioName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RecallExplainsWhatHelpedAndWhatHurt()
    {
        var dir = CreateTempDirectory();
        try
        {
            var memory = CreateMemory(Path.Combine(dir, "episodes.jsonl"));
            for (var i = 0; i < 4; i++)
            {
                memory.Remember(BuildReport(
                    i,
                    "mixed_adaptive",
                    0.01,
                    actionRewards: new Dictionary<string, double>
                    {
                        ["focus_widen"] = 0.04,
                        ["focus_narrow"] = -0.02
                    }));
            }

            var explanation = memory.Explain(new MemoryCue("mixed_adaptive", "calm", 0.2, 0.8, 0.3));

            Assert.Equal("focus_widen", explanation.HelpfulAction);
            Assert.Equal("focus_narrow", explanation.HarmfulAction);
            Assert.True(explanation.HelpfulScore > 0);
            Assert.True(explanation.HarmfulScore < 0);
            Assert.True(explanation.Confidence > 0);
            Assert.NotEmpty(explanation.Evidence);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RepeatedScenarioFormsStableExperience()
    {
        var dir = CreateTempDirectory();
        try
        {
            var memory = CreateMemory(Path.Combine(dir, "episodes.jsonl"));
            for (var i = 0; i < 5; i++)
                memory.Remember(BuildReport(i, "social_pull", 0.02));

            var experience = Assert.Single(memory.GetScenarioExperiences());

            Assert.True(experience.IsMature);
            Assert.Equal(5, experience.EpisodeCount);
            Assert.Equal(1.0, experience.SuccessRate, 6);
            Assert.Equal("focus_widen", experience.HelpfulAction);
            Assert.True(experience.Confidence > 0.5);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FiveThousandEntriesUseBoundedIndexInsteadOfFullScan()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "episodes.jsonl");
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var now = DateTimeOffset.UtcNow;
            File.WriteAllLines(path, Enumerable.Range(0, 5000).Select(i => JsonSerializer.Serialize(
                new EpisodicMemoryEntry(
                    1,
                    i,
                    $"scenario_{i % 50}",
                    now.AddMinutes(-2),
                    now.AddMinutes(-1),
                    100,
                    "timeout",
                    0.01,
                    1,
                    new Dictionary<string, int> { [i % 2 == 0 ? "focus_widen" : "rest_short"] = 100 },
                    i % 2 == 0 ? "calm" : "neutral",
                    (i % 5) / 5.0,
                    ((i / 5) % 5) / 5.0,
                    ((i / 25) % 5) / 5.0,
                    0,
                    true,
                    0.5),
                options)));
            var config = BuildConfig(path) with
            {
                MaxEpisodes = 5000,
                MaxIndexedCandidates = 64,
                ConsolidateEveryEpisodes = 100_000
            };
            var memory = new PersistentEpisodicMemory(_ => { });
            memory.Configure(config);

            memory.BuildActionBias(new MemoryCue("scenario_1", "neutral", 0.2, 0.2, 0.2));
            var stats = memory.GetStatistics();

            Assert.Equal(5000, stats.EntryCount);
            Assert.InRange(stats.LastCandidateCount, 1, 64);
            Assert.True(stats.IndexBuckets > 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VersionOneJsonWithoutCognitiveFieldsStillLoads()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "episodes.jsonl");
            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"episodeId\":7,\"scenarioName\":\"calm_baseline\",\"startTs\":\"2026-01-01T00:00:00Z\",\"endTs\":\"2026-01-01T00:01:00Z\",\"steps\":100,\"endReason\":\"timeout\",\"avgReward\":0.02,\"totalReward\":2,\"actionHistogram\":{\"focus_widen\":100},\"dominantMood\":\"calm\",\"avgPain\":0.1,\"avgSafety\":0.9,\"avgArousal\":0.2,\"loopCount\":0,\"scenarioPassed\":true,\"salience\":0.7}" + Environment.NewLine);
            var memory = CreateMemory(path);

            var entry = Assert.Single(memory.GetRecent(5));

            Assert.Equal(1, entry.Occurrences);
            Assert.True(entry.Strength > 0);
            Assert.Empty(entry.ActionRewardAverages);
            Assert.False(string.IsNullOrWhiteSpace(entry.Fingerprint));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AccumulatedExperienceChoosesBetterActionThanCleanMemory()
    {
        var cleanDir = CreateTempDirectory();
        var learnedDir = CreateTempDirectory();
        try
        {
            var cue = new MemoryCue("mixed_adaptive", "calm", 0.2, 0.8, 0.3);
            var clean = CreateMemory(Path.Combine(cleanDir, "episodes.jsonl"));
            var learned = CreateMemory(Path.Combine(learnedDir, "episodes.jsonl"));
            for (var i = 0; i < 6; i++)
            {
                learned.Remember(BuildReport(
                    i,
                    "mixed_adaptive",
                    0.01,
                    actionRewards: new Dictionary<string, double>
                    {
                        ["adaptive_help"] = 0.04,
                        ["repeat_bad"] = -0.02
                    }));
            }

            var cleanBias = clean.BuildActionBias(cue).ActionBiases;
            var learnedBias = learned.BuildActionBias(cue).ActionBiases;
            var baseScores = new Dictionary<string, double>
            {
                ["repeat_bad"] = 0.501,
                ["adaptive_help"] = 0.500
            };
            var cleanChoice = Choose(baseScores, cleanBias);
            var learnedChoice = Choose(baseScores, learnedBias);
            var expectedOutcome = new Dictionary<string, double>
            {
                ["repeat_bad"] = -0.02,
                ["adaptive_help"] = 0.04
            };

            Assert.Equal("repeat_bad", cleanChoice);
            Assert.Equal("adaptive_help", learnedChoice);
            Assert.True(expectedOutcome[learnedChoice] > expectedOutcome[cleanChoice]);
        }
        finally
        {
            Directory.Delete(cleanDir, recursive: true);
            Directory.Delete(learnedDir, recursive: true);
        }
    }

    private static string Choose(
        IReadOnlyDictionary<string, double> baseScores,
        IReadOnlyDictionary<string, double> biases)
    {
        return baseScores
            .OrderByDescending(pair => pair.Value + (biases.TryGetValue(pair.Key, out var bias) ? bias : 0))
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .First()
            .Key;
    }

    private static PersistentEpisodicMemory CreateMemory(string path)
    {
        var memory = new PersistentEpisodicMemory(_ => { });
        memory.Configure(BuildConfig(path));
        return memory;
    }

    private static MemoryConfig BuildConfig(string path)
    {
        return new MemoryConfig(
            Enable: true,
            Path: path,
            MaxEpisodes: 5000,
            RecallTopK: 8,
            MinSimilarity: 0.45,
            MaxActionBias: 0.15,
            RecencyHalfLifeDays: 90,
            MinEpisodesBeforeBias: 1,
            MinEpisodeSteps: 1,
            RewardScale: 0.02)
        {
            DeduplicationSimilarity = 0.96,
            ConsolidationSimilarity = 0.90,
            ConsolidateEveryEpisodes = 1000,
            ForgetAfterDays = 180,
            ForgetThreshold = 0.12,
            RepeatBoost = 0.08,
            ExperienceMinOccurrences = 3,
            ExperienceWeight = 0.35,
            MaxIndexedCandidates = 512
        };
    }

    private static EpisodeReport BuildReport(
        int episodeId,
        string scenario,
        double avgReward,
        bool passed = true,
        double avgPain = 0.2,
        string endReason = "timeout",
        DateTimeOffset? endTs = null,
        Dictionary<string, double>? actionRewards = null)
    {
        var rewards = actionRewards ?? new Dictionary<string, double> { ["focus_widen"] = avgReward };
        var actions = rewards.Keys.ToDictionary(action => action, _ => 50, StringComparer.Ordinal);
        var end = endTs ?? DateTimeOffset.UtcNow;
        return new EpisodeReport(
            EpisodeId: episodeId,
            ScenarioName: scenario,
            StartTs: end.AddMinutes(-1),
            EndTs: end,
            Steps: 100,
            EndReason: endReason,
            AvgReward: avgReward,
            TotalReward: avgReward * 100,
            RewardBreakdownAvg: new RewardDto(avgReward, 0, 0, 0, 0, avgReward),
            ActionHistogram: actions,
            LoopCount: endReason == "loop" ? 1 : 0,
            MaxLoopStrength: endReason == "loop" ? 1 : 0,
            MoodDistribution: new Dictionary<string, int> { ["calm"] = 100 },
            AvgPain: avgPain,
            MaxPain: avgPain,
            AvgSafety: 0.8,
            AvgArousal: 0.3,
            SocialSignalsSent: 0,
            SelfTalkCount: 0,
            MaskFallbackCount: 0,
            InvalidActionCount: 0,
            MlUsed: true,
            BackendKind: "remote",
            EpsilonUsed: 0.05,
            ScenarioScore: new ScenarioScore(passed, passed ? "ok" : "failed"))
        {
            ActionRewardAverages = new Dictionary<string, double>(rewards, StringComparer.Ordinal)
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "deepbrain-memory-v2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
