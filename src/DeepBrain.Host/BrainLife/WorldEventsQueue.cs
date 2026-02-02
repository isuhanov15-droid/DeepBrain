using System.Linq;
using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

public sealed class WorldEventsQueue
{
    private readonly List<WorldEventDto> _events = new(50);
    private const int Capacity = 50;
    private const double SalienceTauSeconds = 20.0;
    private int _consumedCount;

    public int ConsumedCount => _consumedCount;

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
            if (ev.Consumed)
                salience *= 0.05;
            _events[i] = ev with { AgeSeconds = age, Salience = salience };
        }
    }

    public IReadOnlyList<WorldEventDto> GetRecent(int k)
    {
        if (_events.Count == 0) return Array.Empty<WorldEventDto>();
        var take = Math.Min(k, _events.Count);
        return _events
            .Where(e => !e.Consumed)
            .OrderByDescending(e => e.Salience)
            .ThenByDescending(e => e.Tick)
            .Take(take)
            .ToList();
    }

    public int Consume(Func<WorldEventDto, bool> predicate)
    {
        var count = 0;
        for (var i = 0; i < _events.Count; i++)
        {
            var ev = _events[i];
            if (ev.Consumed) continue;
            if (!predicate(ev)) continue;
            _events[i] = ev with { Consumed = true, Salience = 0.0 };
            count++;
        }

        if (count > 0)
            _consumedCount += count;
        return count;
    }
}
