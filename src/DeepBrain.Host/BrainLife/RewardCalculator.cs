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

    public RewardDto Compute(
        HomeostasisDto before,
        HomeostasisDto after,
        string actionName,
        AppraisalDto appraisal,
        double loopStrength,
        bool invalidAction,
        RewardConfig config)
    {
        return _engine.Compute(before, after, actionName, appraisal, loopStrength, invalidAction, config);
    }
}
