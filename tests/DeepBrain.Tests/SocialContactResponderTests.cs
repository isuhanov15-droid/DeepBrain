using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.BrainDtos.V4;
using Xunit;

namespace DeepBrain.Tests;

public sealed class SocialContactResponderTests
{
    [Fact]
    public void ConsumesOnlyActionableSocialContacts()
    {
        var events = new WorldEventsQueue();
        events.Push(Event("novelty_opportunity", 0.6));
        events.Push(Event("social_ping", 0.5));

        Assert.True(SocialContactResponder.HasPending(events));
        Assert.Equal(1, SocialContactResponder.ConsumePending(events));
        Assert.False(SocialContactResponder.HasPending(events));
        Assert.Equal(0, SocialContactResponder.ConsumePending(events));
        Assert.Equal(1, events.ConsumedCount);
    }

    [Fact]
    public void IgnoresExpiredSocialContact()
    {
        var events = new WorldEventsQueue();
        events.Push(Event("social_ping", 0.3));

        Assert.False(SocialContactResponder.HasPending(events));
        Assert.Equal(0, SocialContactResponder.ConsumePending(events));
    }

    private static WorldEventDto Event(string type, double salience)
    {
        return new WorldEventDto(1, type, salience, "test", 0, salience, false);
    }
}
