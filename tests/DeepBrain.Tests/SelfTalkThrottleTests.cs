using DeepBrain.Host.BrainLife;
using Xunit;

namespace DeepBrain.Tests;

public sealed class SelfTalkThrottleTests
{
    [Fact]
    public void SameLoopMessageIsDeduped()
    {
        var throttle = new SelfTalkThrottle(120, 900, 600);

        Assert.True(throttle.ShouldSpeak(100, "loop", "entered_loop"));
        Assert.False(throttle.ShouldSpeak(160, "loop", "entered_loop"));
        Assert.True(throttle.ShouldSpeak(1200, "loop", "entered_loop"));
    }

    [Fact]
    public void SemanticCooldownBlocksRepeatedEvents()
    {
        var throttle = new SelfTalkThrottle(120, 900, 600);

        Assert.True(throttle.ShouldSpeak(100, "recovered", "recovered_from_loop"));
        Assert.False(throttle.ShouldSpeak(220, "new recovered text", "recovered_from_loop"));
        Assert.True(throttle.ShouldSpeak(800, "new recovered text", "recovered_from_loop"));
    }

    [Fact]
    public void SelfTalkAppearsOnLoopEnterAndRecovery()
    {
        var engine = new SelfTalkEngine();

        var enter = engine.MaybeSpeak(new SelfTalkContext(
            EnteredLoop: true,
            LoopTypeChanged: false,
            LoopRecovered: false,
            CalmWindowEntered: false,
            LoopEndedByEpisode: false,
            LoopType: "repeat",
            LoopStrength: 0.8,
            LoopMinStrength: 0.65), "calm", 10);
        var recover = engine.MaybeSpeak(new SelfTalkContext(
            EnteredLoop: false,
            LoopTypeChanged: false,
            LoopRecovered: true,
            CalmWindowEntered: false,
            LoopEndedByEpisode: false,
            LoopType: "none",
            LoopStrength: 0.0,
            LoopMinStrength: 0.65), "calm", 11);

        Assert.False(string.IsNullOrWhiteSpace(enter));
        Assert.Equal("Петля отпустила. Держу новый курс.", recover);
    }
}
