using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed class RewardEngine
{
    public RewardDto Compute(
        HomeostasisDto before,
        HomeostasisDto after,
        string actionName,
        AppraisalDto appraisal,
        bool isInLoop,
        double loopStrength,
        bool invalidAction,
        RewardConfig config)
    {
        var homeostasis =
            (after.Energy - before.Energy) +
            (after.Safety - before.Safety) -
            (after.Fatigue - before.Fatigue) * 0.5 -
            (after.Pain - before.Pain) * 0.5;

        homeostasis *= config.HomeostasisWeight;

        var explore = 0.0;
        if (actionName is "explore_signal" or "focus_widen" or "loop_break")
            explore = config.ExploreBase + appraisal.Novelty * 0.02;
        explore *= config.ExploreWeight;

        var social = 0.0;
        if (actionName is "emit_message")
            social = config.SocialBase + appraisal.Social * 0.02;
        social *= config.SocialWeight;

        var loopPenalty = isInLoop && loopStrength >= config.LoopPenaltyThreshold
            ? -LifeMath.Clamp01(loopStrength) * config.LoopPenaltyWeight
            : 0.0;
        var invalidPenalty = invalidAction ? -Math.Abs(config.InvalidActionPenalty) : 0.0;

        var total = homeostasis + explore + social + loopPenalty + invalidPenalty;
        total = Math.Clamp(total, -1.0, 1.0);

        return new RewardDto(homeostasis, explore, social, loopPenalty, invalidPenalty, total);
    }
}
