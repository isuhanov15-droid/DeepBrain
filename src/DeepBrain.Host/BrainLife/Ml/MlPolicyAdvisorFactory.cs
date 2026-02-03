namespace DeepBrain.Host.BrainLife.Ml;

public static class MlPolicyAdvisorFactory
{
    public static IMlPolicyAdvisor Create(bool mlCoreAvailable, MlConfig config, Action<string> log)
    {
#if ML_CORE
        if (mlCoreAvailable)
            return new MlPolicyAdvisorReal(config, log);
#endif
        return new MlPolicyAdvisorStub();
    }
}
