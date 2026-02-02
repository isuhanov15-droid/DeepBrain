using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class ActionSelector
{
    private sealed record Candidate(ActionDto Action, double Score, string Reason);

    public (ActionDto action, string reason) Choose(
        HomeostasisDto homeo,
        InstinctsDto instincts,
        AffectDto affect,
        LearningEngine learning)
    {
        var list = new List<Candidate>();

        if (instincts.SelfPreservation > 0.6)
        {
            list.Add(Make("internal", "breathe_slow", instincts.SelfPreservation, "self_preservation", learning));
            list.Add(Make("internal", "focus_narrow", instincts.SelfPreservation * 0.8, "self_preservation", learning));
        }

        if (instincts.EnergyConservation > 0.6)
            list.Add(Make("internal", "rest_short", instincts.EnergyConservation, "energy_conservation", learning));

        if (instincts.Exploration >= 0.6)
        {
            list.Add(Make("external", "explore_signal", instincts.Exploration, "exploration", learning));
            list.Add(Make("internal", "focus_widen", instincts.Exploration * 0.8, "exploration", learning));
        }

        if (instincts.Attachment >= 0.6)
            list.Add(Make("external", "emit_message", instincts.Attachment, "attachment", learning));

        if (affect.Mood == "frustrated" || instincts.Agency > 0.6)
        {
            list.Add(Make("internal", "reframe_negative", instincts.Agency, "agency", learning));
            list.Add(Make("internal", "focus_narrow", instincts.Agency * 0.7, "agency", learning));
        }

        if (list.Count == 0)
            list.Add(Make("internal", "rest_short", 0.2 + (1 - homeo.Energy), "fallback", learning));

        var best = list
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Action.Name, StringComparer.Ordinal)
            .First();

        return (best.Action, best.Reason);
    }

    private static Candidate Make(string kind, string name, double baseScore, string reason, LearningEngine learning)
    {
        var bias = learning.GetEma(name);
        var externalBonus = kind == "external" ? 0.2 : 0.0;
        var score = baseScore + bias * 0.2 + externalBonus;
        var action = new ActionDto(kind, name, LifeMath.Clamp01(baseScore), null);
        return new Candidate(action, score, reason);
    }
}
