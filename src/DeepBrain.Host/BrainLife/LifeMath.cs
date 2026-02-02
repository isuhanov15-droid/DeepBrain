namespace DeepBrain.Host.BrainLife;

public static class LifeMath
{
    public static double Clamp01(double v)
    {
        if (v < 0) return 0;
        if (v > 1) return 1;
        return v;
    }
}
