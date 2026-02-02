using System.Linq;
using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

public sealed class SemanticMemory
{
    private sealed record Stat(double Sum, int Count)
    {
        public double Avg => Count == 0 ? 0 : Sum / Count;
    }

    private readonly Dictionary<string, Dictionary<string, Stat>> _map = new();

    public void Update(string key, string actionName, double reward)
    {
        if (!_map.TryGetValue(key, out var actions))
        {
            actions = new Dictionary<string, Stat>(StringComparer.Ordinal);
            _map[key] = actions;
        }

        if (!actions.TryGetValue(actionName, out var stat))
            stat = new Stat(0, 0);

        actions[actionName] = stat with { Sum = stat.Sum + reward, Count = stat.Count + 1 };
    }

    public (double bonus, double penalty) SuggestBonus(string key, string actionName)
    {
        if (!_map.TryGetValue(key, out var actions)) return (0, 0);
        if (!actions.TryGetValue(actionName, out var stat)) return (0, 0);

        var avg = stat.Avg;
        if (avg > 0.05) return (0.2, 0);
        if (avg < -0.05) return (0, 0.2);
        return (0, 0);
    }

    public IReadOnlyList<SemanticNoteDto> GetTopNotes(int topK)
    {
        var notes = new List<SemanticNoteDto>();
        foreach (var (key, actions) in _map)
        {
            var best = actions.OrderByDescending(kv => kv.Value.Avg).FirstOrDefault();
            if (best.Key is null) continue;
            notes.Add(new SemanticNoteDto(key, best.Key, best.Value.Avg, best.Value.Count));
        }

        return notes
            .OrderByDescending(n => n.Score)
            .Take(topK)
            .ToList();
    }

    public void Consolidate()
    {
        var keys = _map.Keys.ToList();
        foreach (var key in keys)
        {
            var actions = _map[key];
            var actionKeys = actions.Keys.ToList();
            foreach (var a in actionKeys)
            {
                var stat = actions[a];
                if (stat.Count < 2)
                    actions.Remove(a);
                else
                    actions[a] = stat with { Sum = stat.Sum * 0.95 };
            }

            if (actions.Count == 0)
                _map.Remove(key);
        }
    }
}
