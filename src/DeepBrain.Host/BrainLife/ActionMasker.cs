using DeepBrain.Shared.Brain;
using DeepBrain.Host.BrainLife.Ml;

namespace DeepBrain.Host.BrainLife;

public sealed class ActionMasker
{
    public float[] BuildMask(
        HomeostasisDto homeo,
        AffectDto affect,
        ActionCooldowns cooldowns,
        long tick,
        int emitCooldownTicks,
        ActionsConfig actions,
        bool allowLoopBreak)
    {
        var mask = new float[ActionCatalog.Count];
        for (var i = 0; i < mask.Length; i++)
            mask[i] = 1f;

        DisableIf(mask, "rest_short", homeo.Energy > 0.98 && homeo.Fatigue < 0.15);
        DisableIf(mask, "breathe_slow", affect.Arousal < 0.15);
        DisableIf(mask, "reframe_negative", affect.Valence > 0.20);
        DisableIf(mask, "explore_signal", homeo.Safety < 0.25);
        DisableIf(mask, "focus_narrow", affect.Arousal > 0.95);

        if (!allowLoopBreak)
            DisableIf(mask, "loop_break", true);

        ApplyCooldown(mask, "breathe_slow", cooldowns, tick, actions.BreatheSlow);
        ApplyCooldown(mask, "rest_short", cooldowns, tick, actions.RestShort);
        ApplyCooldown(mask, "reframe_negative", cooldowns, tick, actions.ReframeNegative);
        ApplyCooldown(mask, "focus_narrow", cooldowns, tick, actions.FocusNarrow);
        ApplyCooldown(mask, "focus_widen", cooldowns, tick, actions.FocusWiden);
        ApplyCooldown(mask, "explore_signal", cooldowns, tick, actions.ExploreSignal);
        ApplyCooldown(mask, "emit_message", cooldowns, tick, emitCooldownTicks);

        return mask;
    }

    private static void ApplyCooldown(float[] mask, string action, ActionCooldowns cooldowns, long tick, int cd)
    {
        if (cd <= 0) return;
        if (!cooldowns.IsOnCooldown(action, tick, cd)) return;
        DisableIf(mask, action, true);
    }

    private static void DisableIf(float[] mask, string actionName, bool condition)
    {
        if (!condition) return;
        var idx = ActionCatalog.IndexOf(actionName);
        if (idx >= 0 && idx < mask.Length)
            mask[idx] = 0f;
    }
}
