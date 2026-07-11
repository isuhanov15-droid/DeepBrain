using DeepBrain.Host.BrainLife;
using Xunit;

namespace DeepBrain.Tests;

public sealed class EpisodeManagerTests
{
    [Fact]
    public void PanicRequiresConsecutiveCriticalTicks()
    {
        var manager = new EpisodeManager(1200);
        manager.Configure(BrainConfig.Default.Episode with { PanicHoldTicks = 3 });

        Assert.False(manager.Tick(0, false, true, out var firstReason));
        Assert.Null(firstReason);
        Assert.False(manager.Tick(0, false, true, out var secondReason));
        Assert.Null(secondReason);
        Assert.True(manager.Tick(0, false, true, out var finalReason));
        Assert.Equal("panic", finalReason);
    }

    [Fact]
    public void EpisodeResetClearsPendingPanicEvidence()
    {
        var manager = new EpisodeManager(1200);
        manager.Configure(BrainConfig.Default.Episode with { PanicHoldTicks = 3 });

        Assert.False(manager.Tick(0, false, true, out _));
        Assert.False(manager.Tick(0, false, true, out _));
        manager.Reset("panic");

        Assert.False(manager.Tick(0, false, true, out _));
        Assert.False(manager.Tick(0, false, true, out _));
        Assert.True(manager.Tick(0, false, true, out var reason));
        Assert.Equal("panic", reason);
    }
}
