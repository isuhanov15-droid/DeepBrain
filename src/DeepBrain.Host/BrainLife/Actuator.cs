using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class Actuator
{
    public OutcomeDto Apply(ActionDto action, ref HomeostasisDto homeo, ref AffectDto affect)
    {
        string? message = null;

        switch (action.Name)
        {
            case "rest_short":
                homeo = homeo with
                {
                    Energy = LifeMath.Clamp01(homeo.Energy + 0.04 * action.Strength),
                    Fatigue = LifeMath.Clamp01(homeo.Fatigue - 0.03 * action.Strength),
                    Arousal = LifeMath.Clamp01(homeo.Arousal - 0.01 * action.Strength)
                };
                break;

            case "breathe_slow":
                homeo = homeo with
                {
                    Arousal = LifeMath.Clamp01(homeo.Arousal - 0.05 * action.Strength),
                    Safety = LifeMath.Clamp01(homeo.Safety + 0.02 * action.Strength),
                    Pain = LifeMath.Clamp01(homeo.Pain - 0.01 * action.Strength)
                };
                break;

            case "focus_narrow":
                homeo = homeo with
                {
                    Arousal = LifeMath.Clamp01(homeo.Arousal + 0.05 * action.Strength)
                };
                break;

            case "focus_widen":
                homeo = homeo with
                {
                    Arousal = LifeMath.Clamp01(homeo.Arousal - 0.03 * action.Strength)
                };
                break;

            case "reframe_negative":
                affect = affect with
                {
                    Valence = Math.Min(1, affect.Valence + 0.03 * action.Strength)
                };
                break;

            case "recall_safe_memory":
                homeo = homeo with
                {
                    Safety = LifeMath.Clamp01(homeo.Safety + 0.06 * action.Strength),
                    Pain = LifeMath.Clamp01(homeo.Pain - 0.05 * action.Strength)
                };
                break;

            case "explore_signal":
                break;

            case "emit_message":
                message = "seeking contact";
                break;
        }

        return new OutcomeDto(action, 0.0, message);
    }
}
