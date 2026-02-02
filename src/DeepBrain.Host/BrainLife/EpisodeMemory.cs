using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class EpisodeMemory
{
    private readonly List<EpisodeDto> _episodes = new(1000);
    private readonly List<MemoryRecord> _records = new(1000);
    private const int Capacity = 1000;

    public void Add(EpisodeDto ep)
    {
        if (_episodes.Count >= Capacity)
            _episodes.RemoveAt(0);
        _episodes.Add(ep);
    }

    public void Add(EpisodeDto ep, string goalId, string strategy)
    {
        Add(ep);
        if (_records.Count >= Capacity)
            _records.RemoveAt(0);
        _records.Add(new MemoryRecord(goalId, strategy, ep.Action.Name, ep.Reward));
    }

    public IReadOnlyList<EpisodeDto> QuerySimilar(HomeostasisDto homeo, AffectDto affect, int topK)
    {
        if (_episodes.Count == 0) return Array.Empty<EpisodeDto>();

        var scored = new List<(EpisodeDto ep, double score)>(_episodes.Count);
        foreach (var ep in _episodes)
        {
            var h = ep.BeforeHomeostasis;
            var dist =
                Math.Abs(h.Energy - homeo.Energy) +
                Math.Abs(h.Fatigue - homeo.Fatigue) +
                Math.Abs(h.Safety - homeo.Safety) +
                Math.Abs(h.Arousal - homeo.Arousal);

            scored.Add((ep, dist));
        }

        return scored
            .OrderBy(s => s.score)
            .Take(Math.Max(1, topK))
            .Select(s => s.ep)
            .ToList();
    }

    public Dictionary<string, double> Consolidate()
    {
        var sums = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var r in _records)
        {
            if (!sums.ContainsKey(r.Action))
                sums[r.Action] = 0;
            sums[r.Action] += r.Reward;
        }
        return sums;
    }

    private sealed record MemoryRecord(string GoalId, string Strategy, string Action, double Reward);
}
