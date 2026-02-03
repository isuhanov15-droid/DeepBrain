using System.Linq;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed class ActionSelector
{
    public sealed record Candidate(ActionDto Action, double Score, string Reason);

    public (ActionDto action, string reason, string strategy) Choose(
        HomeostasisDto homeo,
        InstinctsDto instincts,
        AffectDto affect,
        LearningEngine learning,
        LoopDetector loop,
        EpisodeMemory memory,
        ActionCooldowns cooldowns,
        long tick,
        string? habitAction,
        double habitStrength,
        double habitInfluence,
        int emitCooldownTicks,
        bool allowVariety,
        bool calmExploreBoost,
        string dominantDrive,
        string? planStrategy,
        string attentionFocus,
        SemanticMemory semantic,
        string semanticKey,
        AppraisalDto appraisal,
        ActionsConfig actionsConfig)
    {
        var (candidates, strategy) = BuildCandidates(
            homeo,
            instincts,
            affect,
            learning,
            loop,
            memory,
            cooldowns,
            tick,
            habitAction,
            habitStrength,
            habitInfluence,
            emitCooldownTicks,
            allowVariety,
            calmExploreBoost,
            dominantDrive,
            planStrategy,
            attentionFocus,
            semantic,
            semanticKey,
            appraisal,
            actionsConfig
        );

        if (candidates.Count == 0)
            candidates.Add(Make("internal", "rest_short", 0.1 + (1 - homeo.Energy), "cooldown_fallback", learning));

        var best = PickBest(candidates);
        return (best.Action, best.Reason, strategy);
    }

    public (List<Candidate> candidates, string strategy) BuildCandidates(
        HomeostasisDto homeo,
        InstinctsDto instincts,
        AffectDto affect,
        LearningEngine learning,
        LoopDetector loop,
        EpisodeMemory memory,
        ActionCooldowns cooldowns,
        long tick,
        string? habitAction,
        double habitStrength,
        double habitInfluence,
        int emitCooldownTicks,
        bool allowVariety,
        bool calmExploreBoost,
        string dominantDrive,
        string? planStrategy,
        string attentionFocus,
        SemanticMemory semantic,
        string semanticKey,
        AppraisalDto appraisal,
        ActionsConfig actionsConfig)
    {
        var list = new List<Candidate>();

        var strategy = ResolveStrategy(instincts, affect, dominantDrive, loop, planStrategy, calmExploreBoost);

        if (IsAllowed(strategy, "regulate") || instincts.SelfPreservation > 0.6)
        {
            list.Add(Make("internal", "breathe_slow", instincts.SelfPreservation, "self_preservation", learning));
            list.Add(Make("internal", "focus_narrow", instincts.SelfPreservation * 0.8, "self_preservation", learning));
        }

        if (IsAllowed(strategy, "rest") || instincts.EnergyConservation > 0.6)
            list.Add(Make("internal", "rest_short", instincts.EnergyConservation, "energy_conservation", learning));

        if (IsAllowed(strategy, "explore") || instincts.Exploration >= 0.6)
        {
            list.Add(Make("external", "explore_signal", instincts.Exploration, "exploration", learning));
            list.Add(Make("internal", "focus_widen", instincts.Exploration * 0.8, "exploration", learning));
        }

        if (IsAllowed(strategy, "connect") || instincts.Attachment >= 0.6)
            list.Add(Make("external", "emit_message", instincts.Attachment, "attachment", learning));

        if (IsAllowed(strategy, "regulate") || affect.Mood == "frustrated" || instincts.Agency > 0.6)
        {
            list.Add(Make("internal", "reframe_negative", instincts.Agency, "agency", learning));
            list.Add(Make("internal", "focus_narrow", instincts.Agency * 0.7, "agency", learning));
        }

        if (list.Count == 0)
            list.Add(Make("internal", "rest_short", 0.2 + (1 - homeo.Energy), "fallback", learning));

        ApplyLoopPenalty(list, loop);
        ApplyMemoryBias(list, memory, homeo, affect);
        ApplyAttentionBias(list, attentionFocus);
        ApplySemanticBias(list, semantic, semanticKey);
        ApplyAppraisalBias(list, appraisal);
        ApplyHabitBias(list, habitAction, habitStrength, habitInfluence, cooldowns, tick, emitCooldownTicks);
        ApplyVarietyBonus(list, cooldowns, tick, allowVariety);
        ApplyCooldowns(list, cooldowns, tick, emitCooldownTicks, actionsConfig);
        return (list, strategy);
    }

    private static Candidate Make(string kind, string name, double baseScore, string reason, LearningEngine learning)
    {
        var bias = learning.GetEma(name);
        var externalBonus = kind == "external" ? 0.2 : 0.0;
        var score = baseScore + bias * 0.2 + externalBonus;
        var action = new ActionDto(kind, name, LifeMath.Clamp01(baseScore), null);
        return new Candidate(action, score, reason);
    }

    private static string ResolveStrategy(InstinctsDto instincts, AffectDto affect, string dominantDrive, LoopDetector loop, string? planStrategy, bool calmExploreBoost)
    {
        if (!string.IsNullOrWhiteSpace(planStrategy))
            return planStrategy!;

        if (loop.SameActionStreak >= 5 && loop.AvgRewardShort < 0)
            return dominantDrive == "energy_conservation" ? "rest" : "regulate";

        if (instincts.SelfPreservation > 0.65)
            return "regulate";
        if (instincts.EnergyConservation > 0.65)
            return "rest";
        if (instincts.Attachment > 0.6)
            return "connect";
        if (instincts.Exploration > 0.6)
            return "explore";
        if (instincts.Agency > 0.6 && affect.Valence < 0)
            return "regulate";

        if (calmExploreBoost)
            return dominantDrive == "exploration" ? "explore" : "focus";

        return dominantDrive == "exploration" ? "explore" : "focus";
    }

    private static bool IsAllowed(string activeStrategy, string candidateStrategy)
    {
        return activeStrategy == candidateStrategy;
    }

    private static void ApplyLoopPenalty(List<Candidate> list, LoopDetector loop)
    {
        if (loop.LoopPenalty <= 0) return;

        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            var penalty = loop.LoopPenalty * 0.5;
            if (loop.LoopPenalty > 0.4 && c.Action.Name == loop.LastAction)
                penalty = 2.0;
            list[i] = c with { Score = c.Score - penalty };
        }
    }

    private static void ApplyMemoryBias(List<Candidate> list, EpisodeMemory memory, HomeostasisDto homeo, AffectDto affect)
    {
        var similar = memory.QuerySimilar(homeo, affect, topK: 12);
        if (similar.Count == 0) return;

        var stats = similar
            .GroupBy(e => e.Action.Name)
            .ToDictionary(g => g.Key, g => g.Average(e => e.Reward));

        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (stats.TryGetValue(c.Action.Name, out var avg))
            {
                var bonus = avg > 0 ? 0.15 : 0;
                var penalty = avg < 0 ? 0.2 : 0;
                list[i] = c with { Score = c.Score + bonus - penalty };
            }
        }
    }

    private static void ApplyAttentionBias(List<Candidate> list, string focus)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            var bonus = 0.0;
            if (focus == "threat" && (c.Action.Name == "breathe_slow" || c.Action.Name == "focus_narrow"))
                bonus = 0.3;
            else if (focus == "novelty" && c.Action.Name == "explore_signal")
                bonus = 0.25;
            else if (focus == "social" && c.Action.Name == "emit_message")
                bonus = 0.25;
            else if (focus == "body" && c.Action.Name == "rest_short")
                bonus = 0.2;
            else if (focus == "agency" && c.Action.Name == "reframe_negative")
                bonus = 0.2;

            if (bonus > 0)
                list[i] = c with { Score = c.Score + bonus };
        }
    }

    private static void ApplySemanticBias(List<Candidate> list, SemanticMemory semantic, string semanticKey)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            var (bonus, penalty) = semantic.SuggestBonus(semanticKey, c.Action.Name);
            if (bonus != 0 || penalty != 0)
                list[i] = c with { Score = c.Score + bonus - penalty };
        }
    }

    private static void ApplyAppraisalBias(List<Candidate> list, AppraisalDto appraisal)
    {
        if (appraisal.Threat > 0.6)
            Boost(list, new[] { "breathe_slow", "focus_narrow" }, 0.2);
        if (appraisal.Novelty > 0.6)
            Boost(list, new[] { "explore_signal", "focus_widen" }, 0.15);
        if (appraisal.Social > 0.6)
            Boost(list, new[] { "emit_message" }, 0.15);
        if (appraisal.Fatigue > 0.6)
            Boost(list, new[] { "rest_short" }, 0.2);
    }

    private static void Boost(List<Candidate> list, IEnumerable<string> names, double bonus)
    {
        foreach (var name in names)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c.Action.Name != name) continue;
                list[i] = c with { Score = c.Score + bonus };
            }
        }
    }

    private static void ApplyHabitBias(
        List<Candidate> list,
        string? habitAction,
        double habitStrength,
        double habitInfluence,
        ActionCooldowns cooldowns,
        long tick,
        int emitCooldownTicks)
    {
        if (string.IsNullOrWhiteSpace(habitAction)) return;
        var cooldown = habitAction == "emit_message" ? emitCooldownTicks : 2;
        if (cooldown > 0 && cooldowns.IsOnCooldown(habitAction, tick, cooldown)) return;

        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c.Action.Name != habitAction) continue;
            var bonus = 0.05 + habitStrength * 0.1;
            list[i] = c with { Score = c.Score + bonus * LifeMath.Clamp01(habitInfluence) };
            break;
        }
    }

    private static void ApplyVarietyBonus(List<Candidate> list, ActionCooldowns cooldowns, long tick, bool allowVariety)
    {
        if (!allowVariety) return;
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            var last = cooldowns.GetLastTick(c.Action.Name);
            var gap = last < 0 ? 100 : tick - last;
            if (gap < 10) continue;
            var bonus = Math.Min(0.08, (gap / 15.0) * 0.05);
            list[i] = c with { Score = c.Score + bonus };
        }
    }

    private static void ApplyCooldowns(List<Candidate> list, ActionCooldowns cooldowns, long tick, int emitCooldownTicks, ActionsConfig actions)
    {
        var filtered = list.Where(c => !IsOnCooldown(cooldowns, c.Action.Name, tick, emitCooldownTicks, actions)).ToList();
        list.Clear();
        list.AddRange(filtered);
    }

    private static bool IsOnCooldown(ActionCooldowns cooldowns, string actionName, long tick, int emitCooldownTicks, ActionsConfig actions)
    {
        var cd = actionName switch
        {
            "breathe_slow" => actions.BreatheSlow,
            "rest_short" => actions.RestShort,
            "reframe_negative" => actions.ReframeNegative,
            "focus_narrow" => actions.FocusNarrow,
            "focus_widen" => actions.FocusWiden,
            "explore_signal" => actions.ExploreSignal,
            "emit_message" => emitCooldownTicks,
            _ => 0
        };

        return cd > 0 && cooldowns.IsOnCooldown(actionName, tick, cd);
    }

    public static Candidate PickBest(IReadOnlyList<Candidate> list)
    {
        return list
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Action.Name, StringComparer.Ordinal)
            .First();
    }
}
