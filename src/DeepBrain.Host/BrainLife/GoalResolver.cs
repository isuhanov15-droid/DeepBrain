using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;

namespace DeepBrain.Host.BrainLife;

public sealed class GoalResolver
{
    private readonly Dictionary<string, double> _satisfaction = new()
    {
        ["regulate"] = 0.5,
        ["restore_energy"] = 0.5,
        ["explore"] = 0.5,
        ["connect"] = 0.5,
        ["focus"] = 0.5
    };

    public IReadOnlyList<GoalDto> Resolve(HomeostasisDto homeo, InstinctsDto instincts, string dominantDrive, string phase)
    {
        DecaySatisfaction(0.01);

        var goals = new List<GoalDto>(4);

        if (dominantDrive == "self_preservation" || instincts.SelfPreservation > 0.6)
            goals.Add(Make("regulate", "homeostatic", instincts.SelfPreservation, "self_preservation"));

        if (dominantDrive == "energy_conservation" || instincts.EnergyConservation > 0.6)
            goals.Add(Make("restore_energy", "homeostatic", instincts.EnergyConservation, "energy"));

        if (phase != "night" && (dominantDrive == "exploration" || instincts.Exploration > 0.5))
            goals.Add(Make("explore", "exploratory", instincts.Exploration, "exploration"));

        if (dominantDrive == "attachment" || instincts.Attachment > 0.55)
            goals.Add(Make("connect", "social", instincts.Attachment, "attachment"));

        if (goals.Count < 2)
            goals.Add(Make("focus", "exploratory", Math.Max(0.2, 1.0 - homeo.Fatigue), "focus"));

        return goals
            .OrderByDescending(g => g.Urgency)
            .Take(4)
            .ToList();
    }

    public void ApplyGoalSatisfaction(string goalId, double delta)
    {
        if (!_satisfaction.ContainsKey(goalId)) _satisfaction[goalId] = 0.5;
        _satisfaction[goalId] = LifeMath.Clamp01(_satisfaction[goalId] + delta);
    }

    private GoalDto Make(string id, string type, double urgency, string source)
    {
        var sat = _satisfaction.TryGetValue(id, out var v) ? v : 0.5;
        return new GoalDto(id, type, LifeMath.Clamp01(urgency), sat, source);
    }

    private void DecaySatisfaction(double amount)
    {
        var keys = _satisfaction.Keys.ToList();
        foreach (var k in keys)
            _satisfaction[k] = LifeMath.Clamp01(_satisfaction[k] - amount);
    }
}
