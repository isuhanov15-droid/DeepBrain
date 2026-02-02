using DeepBrain.Shared.BrainDtos.V3;

namespace DeepBrain.Host.BrainLife;

public sealed class CircadianClock
{
    private const double DayLengthSeconds = 240.0;
    private const double SleepPressureGain = 0.003;
    private const double SleepPressureDecay = 0.01;

    public double TimeOfDay { get; private set; }
    public double SleepPressure { get; private set; }

    public void Tick(double dtSeconds, bool isSleeping)
    {
        TimeOfDay = (TimeOfDay + dtSeconds / DayLengthSeconds) % 1.0;
        if (isSleeping)
            SleepPressure = LifeMath.Clamp01(SleepPressure - SleepPressureDecay);
        else
            SleepPressure = LifeMath.Clamp01(SleepPressure + SleepPressureGain);
    }

    public CircadianDto Snapshot(bool isSleeping)
    {
        return new CircadianDto(GetPhase(TimeOfDay), TimeOfDay, isSleeping, SleepPressure);
    }

    public static string GetPhase(double timeOfDay)
    {
        if (timeOfDay < 0.25) return "morning";
        if (timeOfDay < 0.60) return "active";
        if (timeOfDay < 0.85) return "evening";
        return "night";
    }
}
