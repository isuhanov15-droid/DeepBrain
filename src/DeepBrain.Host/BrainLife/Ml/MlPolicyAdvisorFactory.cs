namespace DeepBrain.Host.BrainLife.Ml;

public static class MlPolicyAdvisorFactory
{
    public static IMlPolicyAdvisor Create(bool mlCoreAvailable, MlConfig config, Action<string> log)
    {
        return new MlPolicyAdvisor(config, log);
    }
}
