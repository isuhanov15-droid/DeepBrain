using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;

namespace DeepBrain.Host.BrainLife;

public sealed class SleepEngine
{
    public bool IsSleeping { get; private set; }
    public bool EnteredSleep { get; private set; }
    public bool WokeUp { get; private set; }

    public void Update(CircadianClock clock, ref HomeostasisDto homeo, ref AffectDto affect)
    {
        EnteredSleep = false;
        WokeUp = false;

        var phase = CircadianClock.GetPhase(clock.TimeOfDay);
        if (!IsSleeping)
        {
            if (phase == "night" && clock.SleepPressure > 0.6 && homeo.Safety > 0.5)
            {
                IsSleeping = true;
                EnteredSleep = true;
            }
        }

        if (IsSleeping)
        {
            homeo = homeo with
            {
                Energy = LifeMath.Clamp01(homeo.Energy + 0.06),
                Fatigue = LifeMath.Clamp01(homeo.Fatigue - 0.05),
                Pain = LifeMath.Clamp01(homeo.Pain - 0.03),
                Arousal = LifeMath.Clamp01(homeo.Arousal - 0.08)
            };
            affect = affect with
            {
                Arousal = LifeMath.Clamp01(affect.Arousal - 0.08)
            };

            if (phase == "morning" && clock.SleepPressure < 0.2)
            {
                IsSleeping = false;
                WokeUp = true;
            }
        }
    }
}
