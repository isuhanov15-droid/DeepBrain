using System.Linq;
using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

public sealed class WorldEventsQueue
{
    private readonly List<WorldEventDto> _events = new(50);
    private const int Capacity = 50;

    public void Push(WorldEventDto ev)
    {
        if (_events.Count >= Capacity)
            _events.RemoveAt(0);
        _events.Add(ev);
    }

    public IReadOnlyList<WorldEventDto> GetRecent(int k)
    {
        if (_events.Count == 0) return Array.Empty<WorldEventDto>();
        var take = Math.Min(k, _events.Count);
        return _events.Skip(_events.Count - take).ToList();
    }
}
