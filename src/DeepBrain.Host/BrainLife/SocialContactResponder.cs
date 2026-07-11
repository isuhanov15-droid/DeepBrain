using System.Linq;
using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

public static class SocialContactResponder
{
    private const double MinimumSalience = 0.3;

    public static bool HasPending(WorldEventsQueue events)
    {
        return events.GetRecent(50).Any(IsActionableContact);
    }

    public static int ConsumePending(WorldEventsQueue events)
    {
        return events.Consume(IsActionableContact);
    }

    private static bool IsActionableContact(WorldEventDto ev)
    {
        return ev.Type == "social_ping" && ev.Salience > MinimumSalience;
    }
}
