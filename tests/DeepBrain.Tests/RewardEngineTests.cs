using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class RewardEngineTests
{
    [Fact]
    public void LoopPenaltyBecomesNegativeAboveThreshold()
    {
        var engine = new RewardEngine();
        var reward = engine.Compute(
            new HomeostasisDto(0.5, 0.5, 0.2, 0.2, 0.7),
            new HomeostasisDto(0.5, 0.5, 0.2, 0.2, 0.7),
            "rest_short",
            new AppraisalDto(0.1, 0.1, 0.1, 0.1),
            true,
            0.8,
            false,
            new RewardConfig(1.0, 1.0, 1.0, 0.05, 0.35, 0.05, 0.01, 0.01));

        Assert.True(reward.LoopPenalty < 0);
    }

    [Fact]
    public void LoopPenaltyIsZeroWhenNotInLoop()
    {
        var engine = new RewardEngine();
        var reward = engine.Compute(
            new HomeostasisDto(0.5, 0.5, 0.2, 0.2, 0.7),
            new HomeostasisDto(0.5, 0.5, 0.2, 0.2, 0.7),
            "rest_short",
            new AppraisalDto(0.1, 0.1, 0.1, 0.1),
            false,
            0.9,
            false,
            new RewardConfig(1.0, 1.0, 1.0, 0.05, 0.35, 0.05, 0.01, 0.01));

        Assert.Equal(0.0, reward.LoopPenalty);
    }
}
