namespace DeepBrain.Host.BrainLife;

public sealed class LearningEngine
{
    private readonly Dictionary<string, double> _emaReward = new();
    private const double Alpha = 0.1;

    public void Update(string actionName, double reward)
    {
        if (_emaReward.TryGetValue(actionName, out var cur))
            _emaReward[actionName] = cur * (1 - Alpha) + reward * Alpha;
        else
            _emaReward[actionName] = reward;
    }

    public double GetEma(string actionName)
    {
        return _emaReward.TryGetValue(actionName, out var v) ? v : 0.0;
    }

    public (string action, double ema) GetBest()
    {
        if (_emaReward.Count == 0) return ("none", 0.0);
        var best = _emaReward.OrderByDescending(kv => kv.Value).First();
        return (best.Key, best.Value);
    }
}
