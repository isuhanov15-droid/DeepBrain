namespace DeepBrain.Host.BrainLife;

public sealed record ScenarioDefinition(
    string Name,
    double BaselineThreat,
    double BaselineTension,
    double NoveltyChance,
    double SocialPingChance,
    double FatigueWaveChance,
    double CalmWindowChance,
    double DriftRate,
    double ShockChance,
    double EpisodeLengthMultiplier = 1.0
)
{
    public WorldConfig Apply(WorldConfig baseConfig)
    {
        return baseConfig with
        {
            BaselineThreat = BaselineThreat,
            BaselineTension = BaselineTension,
            NoveltyChanceBase = NoveltyChance,
            SocialPingChanceBase = SocialPingChance,
            FatigueWaveChanceBase = FatigueWaveChance,
            CalmWindowChanceBase = CalmWindowChance,
            DriftRatePerSec = DriftRate,
            ShockChanceBase = ShockChance
        };
    }
}
