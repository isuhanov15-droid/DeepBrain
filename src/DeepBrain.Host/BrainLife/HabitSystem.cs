using System.Linq;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V5;

namespace DeepBrain.Host.BrainLife;

public sealed class HabitSystem
{
    private sealed record HabitState(string Id, string CueKey, string RoutineAction, double Strength, int Uses, double AvgReward);

    private readonly Dictionary<string, HabitState> _habits = new(StringComparer.Ordinal);

    public HabitSystem()
    {
        SeedDefaults();
    }

    public string ComputeCue(AttentionDto attention, CircadianDto circadian, LoopDetector loop, IReadOnlyList<WorldEventDto> eventsList, HomeostasisDto homeo)
    {
        if (attention.Focus1 == "threat" && attention.Intensity > 0.6)
            return "threat_high";
        if (homeo.Fatigue > 0.6 || circadian.SleepPressure > 0.6)
            return "fatigue_high";
        if (circadian.Phase == "evening" && eventsList.Any(e => e.Type == "social_ping" && e.Salience > 0.3))
            return "evening_social_ping";
        if (eventsList.Any(e => (e.Type == "calm_window" || e.Type == "novelty_opportunity") && e.Salience > 0.3))
            return "calm_novelty_window";
        if (loop.LoopPenalty > 0.5)
            return "loop_warning";

        return "none";
    }

    public (string? actionName, double strength, string? habitId) Suggest(string cueKey)
    {
        if (cueKey == "none") return (null, 0, null);
        if (!_habits.TryGetValue(cueKey, out var habit)) return (null, 0, null);
        return (habit.RoutineAction, habit.Strength, habit.Id);
    }

    public HabitDto? UpdateAfter(string actionName, double reward, string cueKey)
    {
        if (cueKey == "none") return null;
        if (!_habits.TryGetValue(cueKey, out var habit)) return null;

        var strength = habit.Strength;
        if (string.Equals(actionName, habit.RoutineAction, StringComparison.Ordinal))
            strength = LifeMath.Clamp01(strength + (reward >= 0 ? 0.03 : -0.02));
        else if (reward < 0)
            strength = LifeMath.Clamp01(strength - 0.01);

        var uses = habit.Uses + 1;
        var avg = uses == 1 ? reward : habit.AvgReward * 0.9 + reward * 0.1;
        habit = habit with { Strength = strength, Uses = uses, AvgReward = avg };
        _habits[cueKey] = habit;
        return ToDto(habit);
    }

    public IReadOnlyList<HabitDto> GetTopHabits(int k)
    {
        return _habits.Values
            .OrderByDescending(h => h.Strength)
            .Take(k)
            .Select(ToDto)
            .ToList();
    }

    public double ComputeInfluence(PersonalityDto persona, double habitStrength)
    {
        var baseInfluence = LifeMath.Clamp01(0.2 + habitStrength * 0.6);
        var disciplineFactor = LifeMath.Clamp01(1.0 - persona.DisciplineBaseline * 0.6);
        return LifeMath.Clamp01(baseInfluence * disciplineFactor);
    }

    private void SeedDefaults()
    {
        Add("regulate_breathe", "threat_high", "breathe_slow", 0.45);
        Add("rest_recover", "fatigue_high", "rest_short", 0.5);
        Add("check_social", "evening_social_ping", "emit_message", 0.4);
        Add("explore_window", "calm_novelty_window", "explore_signal", 0.35);
        Add("break_loop", "loop_warning", "reframe_negative", 0.4);
    }

    private void Add(string id, string cueKey, string action, double strength)
    {
        _habits[cueKey] = new HabitState(id, cueKey, action, strength, 0, 0);
    }

    private static HabitDto ToDto(HabitState state)
    {
        return new HabitDto(state.Id, state.CueKey, state.RoutineAction, state.Strength, state.Uses, state.AvgReward);
    }
}
