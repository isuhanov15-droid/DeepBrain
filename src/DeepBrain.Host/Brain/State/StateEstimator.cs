using DeepBrain.Host.Brain.Act;
using DeepBrain.Host.Brain.Perception;

namespace DeepBrain.Host.Brain.State;

public sealed class StateEstimator
{
    private const float StressResponse = 0.55f;
    private const float EnergyDrain    = 0.20f;
    private const float FocusGain      = 0.25f;

    private const float StressRecover  = 0.06f;
    private const float EnergyRecover  = 0.04f;
    private const float FocusRecover   = 0.05f;

    public void UpdateHomeostasis(BrainStateInternal state, PerceptionDrives drives, ActResult? lastAct)
    {
        state.Stress = Clamp01(state.Stress + drives.Threat * StressResponse - StressRecover);
        state.Energy = Clamp01(state.Energy - drives.Fatigue * EnergyDrain + EnergyRecover);
        state.Focus  = Clamp01(state.Focus  + drives.GoalPull * FocusGain - FocusRecover);

        // саморегуляция: мягко к базовой линии
        state.Stress = MoveTowards(state.Stress, BrainStateInternal.BaselineStress, 0.02f);
        state.Energy = MoveTowards(state.Energy, BrainStateInternal.BaselineEnergy, 0.02f);
        state.Focus  = MoveTowards(state.Focus,  BrainStateInternal.BaselineFocus,  0.02f);

        state.Mood = ComputeMood(state);
    }

    private static string ComputeMood(BrainStateInternal s)
    {
        if (s.Energy < 0.35f) return "tired";
        if (s.Stress > 0.75f) return "anxious";
        if (s.Focus  > 0.75f && s.Stress < 0.35f) return "focused";
        return "calm";
    }

    private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (current < target) return MathF.Min(current + maxDelta, target);
        if (current > target) return MathF.Max(current - maxDelta, target);
        return current;
    }
}
