using System.Linq;
using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

public sealed class WorldEventsQueue
{
    private readonly List<WorldEventDto> _events = new(50);
    private const int Capacity = 50;
    private const double SalienceTauSeconds = 20.0;

    public void Push(WorldEventDto ev)
    {
        if (_events.Count >= Capacity)
            _events.RemoveAt(0);
        _events.Add(ev);
    }

    public void Tick(double dtSeconds)
    {
        if (_events.Count == 0) return;

        for (var i = 0; i < _events.Count; i++)
        {
            var ev = _events[i];
            var age = Math.Max(0, ev.AgeSeconds + dtSeconds);
            var salience = LifeMath.Clamp01(ev.Severity * Math.Exp(-age / SalienceTauSeconds));
            _events[i] = ev with { AgeSeconds = age, Salience = salience };
        }
    }

    public IReadOnlyList<WorldEventDto> GetRecent(int k)
    {
        if (_events.Count == 0) return Array.Empty<WorldEventDto>();
        var take = Math.Min(k, _events.Count);
        return _events
            .OrderByDescending(e => e.Salience)
            .ThenByDescending(e => e.Tick)
            .Take(take)
            .ToList();
    }
}
