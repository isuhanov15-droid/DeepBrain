using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class PanicRecoveryTests
{
    [Fact]
    public void RestoreMovesHomeostasisOutsidePanicThresholds()
    {
        var config = BrainConfig.Default.Episode;
        var critical = new HomeostasisDto(0.4, 0.8, 0.9, 1.0, 0.02);

        var restored = PanicRecovery.Restore(critical, config);

        Assert.True(restored.Safety > config.PanicSafetyMin);
        Assert.True(restored.Pain < config.PanicPainMin);
        Assert.Equal(config.PanicRecoverySafety, restored.Safety, 6);
        Assert.Equal(config.PanicRecoveryPainMax, restored.Pain, 6);
    }

    [Fact]
    public void PanicTerminalPenaltyMakesPositiveTransitionNegative()
    {
        var positive = new RewardDto(0, 0.02, 0, 0, 0, 0.02);

        var penalized = PanicRecovery.ApplyTerminalPenalty(positive, BrainConfig.Default.Episode);

        Assert.True(penalized.TerminalPenalty < 0);
        Assert.True(penalized.Total < 0);
        Assert.Equal(-0.48, penalized.Total, 6);
    }
}
