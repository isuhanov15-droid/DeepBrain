using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public static class PanicRecovery
{
    public static HomeostasisDto Restore(HomeostasisDto homeostasis, EpisodeConfig config)
    {
        var safetyFloor = Math.Clamp(
            Math.Max(config.PanicRecoverySafety, config.PanicSafetyMin + 0.05),
            0.0,
            1.0);
        var painCeiling = Math.Clamp(
            Math.Min(config.PanicRecoveryPainMax, config.PanicPainMin - 0.05),
            0.0,
            1.0);

        return homeostasis with
        {
            Safety = Math.Max(homeostasis.Safety, safetyFloor),
            Pain = Math.Min(homeostasis.Pain, painCeiling)
        };
    }

    public static RewardDto ApplyTerminalPenalty(RewardDto reward, EpisodeConfig config)
    {
        var penalty = -Math.Abs(config.PanicTerminalPenalty);
        if (penalty == 0)
            return reward;

        return reward with
        {
            TerminalPenalty = reward.TerminalPenalty + penalty,
            Total = Math.Clamp(reward.Total + penalty, -1.0, 1.0)
        };
    }
}
