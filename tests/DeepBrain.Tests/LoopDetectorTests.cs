using DeepBrain.Host.BrainLife;
using Xunit;

namespace DeepBrain.Tests;

public sealed class LoopDetectorTests
{
    [Fact]
    public void RepeatPatternTriggersLoop()
    {
        var loop = new LoopDetector();
        loop.Configure(window: 32, sameK: 6, altK: 6);

        for (var i = 0; i < 6; i++)
        {
            loop.Update(
                actionName: "rest_short",
                mood: "calm",
                arousal: 0.2,
                energy: 0.8,
                fatigue: 0.2,
                dominantDrive: "energy_conservation",
                phase: "morning",
                attentionFocus: "body",
                reward: 0.01,
                tick: i);
        }

        Assert.True(loop.IsLoopDetected);
        Assert.Equal("repeat", loop.LoopType);
        Assert.True(loop.LoopStrength > 0.5);
        Assert.True(loop.EnteredLoop);
        Assert.True(loop.IsInLoop);
    }

    [Fact]
    public void AbabPatternTriggersLoop()
    {
        var loop = new LoopDetector();
        loop.Configure(window: 32, sameK: 8, altK: 3);

        var actions = new[] { "rest_short", "breathe_slow", "rest_short", "breathe_slow", "rest_short", "breathe_slow" };
        for (var i = 0; i < actions.Length; i++)
        {
            loop.Update(
                actionName: actions[i],
                mood: "calm",
                arousal: 0.2,
                energy: 0.8,
                fatigue: 0.2,
                dominantDrive: "energy_conservation",
                phase: "morning",
                attentionFocus: "body",
                reward: 0.01,
                tick: i);
        }

        Assert.True(loop.IsLoopDetected);
        Assert.Equal("abab", loop.LoopType);
    }

    [Fact]
    public void RecoveryTransitionIsReportedWhenPatternStops()
    {
        var loop = new LoopDetector();
        loop.Configure(window: 32, sameK: 4, altK: 3);

        for (var i = 0; i < 4; i++)
        {
            loop.Update("rest_short", "calm", 0.2, 0.8, 0.2, "energy_conservation", "morning", "body", 0.01, i);
        }

        Assert.True(loop.IsInLoop);

        loop.Update("focus_widen", "curious", 0.3, 0.7, 0.3, "exploration", "morning", "novelty", 0.02, 10);

        Assert.False(loop.IsInLoop);
        Assert.True(loop.RecoveredFromLoop);
    }
}
