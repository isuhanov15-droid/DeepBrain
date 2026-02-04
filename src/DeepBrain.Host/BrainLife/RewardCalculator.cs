using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed class RewardCalculator
{
    private readonly RewardEngine _engine;

    public RewardCalculator(RewardEngine engine)
    {
        _engine = engine;
    }

    public RewardDto Compute(HomeostasisDto before, HomeostasisDto after, string actionName, AppraisalDto appraisal, double loopPenalty)
    {
        var homeostasis = _engine.Compute(before, after);
        var explore = actionName is "explore_signal" or "focus_widen"
            ? 0.01 + appraisal.Novelty * 0.02
            : 0.0;
        var social = actionName is "emit_message"
            ? 0.01 + appraisal.Social * 0.02
            : 0.0;

        var loopPenaltyScore = -LifeMath.Clamp01(loopPenalty) * 0.05;
        if (actionName == "loop_break")
            explore += 0.015;

        var total = homeostasis + explore + social + loopPenaltyScore;
        total = Math.Clamp(total, -1.0, 1.0);

        return new RewardDto(homeostasis, explore, social, loopPenaltyScore, total);
    }
}
