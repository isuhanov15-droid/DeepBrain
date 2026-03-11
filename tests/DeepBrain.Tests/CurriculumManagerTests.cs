using System.Collections.Generic;
using DeepBrain.Host.BrainLife;
using Xunit;

namespace DeepBrain.Tests;

public sealed class CurriculumManagerTests
{
    [Fact]
    public void FixedModeDoesNotAdvance()
    {
        var manager = new CurriculumManager(seed: 1);
        manager.Configure(new CurriculumConfig("fixed", 0.0), BuildScenarios());
        var first = manager.ScenarioName;
        var advanced = manager.Advance(1.0);
        Assert.False(advanced);
        Assert.Equal(first, manager.ScenarioName);
    }

    [Fact]
    public void RoundRobinAdvances()
    {
        var manager = new CurriculumManager(seed: 1);
        manager.Configure(new CurriculumConfig("round_robin", 0.0), BuildScenarios());
        var first = manager.ScenarioName;
        var advanced = manager.Advance(1.0);
        Assert.True(advanced);
        Assert.NotEqual(first, manager.ScenarioName);
    }

    [Fact]
    public void RewardGatedBlocksWhenBelowThreshold()
    {
        var manager = new CurriculumManager(seed: 1);
        manager.Configure(new CurriculumConfig("reward_gated", 0.5), BuildScenarios());
        var first = manager.ScenarioName;
        var advanced = manager.Advance(0.1);
        Assert.False(advanced);
        Assert.Equal(first, manager.ScenarioName);
    }

    private static Dictionary<string, ScenarioConfig> BuildScenarios()
    {
        return new Dictionary<string, ScenarioConfig>
        {
            ["calm_baseline"] = new ScenarioConfig(0.1, 0.2, 0.02, 0.03, 0.02, 0.02, 0.02, 0.01, 1.0),
            ["novelty_walk"] = new ScenarioConfig(0.1, 0.2, 0.05, 0.03, 0.02, 0.02, 0.02, 0.01, 1.0)
        };
    }
}
