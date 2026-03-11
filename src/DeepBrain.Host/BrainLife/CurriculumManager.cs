using System.Linq;

namespace DeepBrain.Host.BrainLife;

public sealed class CurriculumManager
{
    private readonly Random _rng;
    private readonly List<ScenarioDefinition> _scenarios = new();
    private string _mode = "fixed";
    private double _rewardGateThreshold;
    private int _index;

    public CurriculumManager(int seed)
    {
        _rng = new Random(seed);
    }

    public string Mode => _mode;
    public int ScenarioIndex => _index;
    public string ScenarioName => _scenarios.Count == 0 ? "none" : _scenarios[_index].Name;

    public void Configure(CurriculumConfig config, IReadOnlyDictionary<string, ScenarioConfig> scenarios)
    {
        _mode = string.IsNullOrWhiteSpace(config.Mode) ? "fixed" : config.Mode.Trim().ToLowerInvariant();
        _rewardGateThreshold = config.RewardGateThreshold;

        var current = ScenarioName;
        _scenarios.Clear();
        foreach (var kv in scenarios.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var def = kv.Value;
            _scenarios.Add(new ScenarioDefinition(
                kv.Key,
                def.BaselineThreat,
                def.BaselineTension,
                def.NoveltyChance,
                def.SocialPingChance,
                def.FatigueWaveChance,
                def.CalmWindowChance,
                def.DriftRate,
                def.ShockChance,
                def.EpisodeLengthMultiplier
            ));
        }

        if (_scenarios.Count == 0)
        {
            _index = 0;
            return;
        }

        var idx = _scenarios.FindIndex(s => s.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
        _index = idx >= 0 ? idx : Math.Clamp(_index, 0, _scenarios.Count - 1);
    }

    public IReadOnlyList<string> ListScenarios()
    {
        return _scenarios.Select(s => s.Name).ToArray();
    }

    public bool TrySetScenario(string name)
    {
        var idx = _scenarios.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;
        _index = idx;
        return true;
    }

    public void SetMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;
        _mode = mode.Trim().ToLowerInvariant();
    }

    public ScenarioDefinition GetScenario()
    {
        if (_scenarios.Count == 0)
            return new ScenarioDefinition("calm_baseline", 0.10, 0.25, 0.02, 0.04, 0.02, 0.02, 0.02, 0.01, 1.0);
        return _scenarios[_index];
    }

    public bool Advance(double avgReward)
    {
        if (_scenarios.Count <= 1) return false;
        if (_mode == "fixed") return false;

        if (_mode == "reward_gated" && avgReward < _rewardGateThreshold)
            return false;

        if (_mode == "random_seeded")
        {
            _index = _rng.Next(0, _scenarios.Count);
            return true;
        }

        _index = (_index + 1) % _scenarios.Count;
        return true;
    }
}
