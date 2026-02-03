#if ML_CORE
namespace DeepBrain.Host.BrainLife.Ml;

public static class MlCoreAvailability
{
    public static readonly bool IsAvailable = true;
}
#else
namespace DeepBrain.Host.BrainLife.Ml;

public static class MlCoreAvailability
{
    public static readonly bool IsAvailable = false;
}
#endif
