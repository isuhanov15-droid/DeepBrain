using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using Xunit;

namespace DeepBrain.Tests;

public sealed class AttentionEngineTests
{
    [Fact]
    public void IncomingContactWinsOverNoveltyWithoutAttachmentThreshold()
    {
        var attention = new AttentionEngine().Compute(
            new HomeostasisDto(0.8, 0.2, 0.2, 0.1, 0.8),
            new InstinctsDto(0.2, 0.2, 0.9, 0.05, 0.2),
            new AffectDto("calm", 0.1, 0.2),
            new CircadianDto("evening", 0.75, false, 0.2),
            new[]
            {
                Event("calm_window", 0.5),
                Event("social_ping", 0.5)
            },
            worldTension: 0.2,
            dtSeconds: 0.2);

        Assert.Equal("social", attention.Focus1);
        Assert.Equal("social_ping", attention.Reason);
    }

    [Fact]
    public void ThreatStillHasPriorityOverIncomingContact()
    {
        var attention = new AttentionEngine().Compute(
            new HomeostasisDto(0.8, 0.2, 0.2, 0.1, 0.8),
            new InstinctsDto(0.2, 0.2, 0.2, 0.05, 0.2),
            new AffectDto("calm", 0.1, 0.2),
            new CircadianDto("evening", 0.75, false, 0.2),
            new[]
            {
                Event("social_ping", 0.5),
                Event("threat_spike", 0.6)
            },
            worldTension: 0.2,
            dtSeconds: 0.2);

        Assert.Equal("threat", attention.Focus1);
    }

    private static WorldEventDto Event(string type, double salience)
    {
        return new WorldEventDto(1, type, salience, "test", 0, salience, false);
    }
}
