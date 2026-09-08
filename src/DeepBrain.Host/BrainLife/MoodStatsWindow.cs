using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed class MoodStatsWindow
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private int _total;

    public void Add(string mood)
    {
        var key = mood is "anxious" or "calm" or "curious" or "neutral" or "tender" or "low" or "frustrated"
            ? mood : "other";
        _counts[key] = _counts.GetValueOrDefault(key) + 1;
        _total++;
    }

    public double Share(string mood) => _total == 0 ? 0 : _counts.GetValueOrDefault(mood) / (double)_total;

    public LifeStatsDto Snapshot(double p95Pain) => new(Share("anxious"), Share("calm"), Share("curious"), p95Pain)
    {
        NeutralScore = Share("neutral"), TenderScore = Share("tender"),
        LowScore = Share("low"), FrustratedScore = Share("frustrated"), OtherScore = Share("other")
    };

    public void Clear()
    {
        _counts.Clear();
        _total = 0;
    }
}
