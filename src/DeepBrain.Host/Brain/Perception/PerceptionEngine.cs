using DeepBrain.Shared.Input;

namespace DeepBrain.Host.Brain.Perception;

public sealed class PerceptionEngine
{
    public (Percept percept, PerceptionDrives drives) Sense(long tick, BrainInputDto input, IReadOnlyList<string> events)
    {
        var threat   = Clamp01(input.Stress);
        var fatigue  = Clamp01(1f - input.Energy);
        var goalPull = Clamp01(input.Focus);

        foreach (var ev in events)
        {
            switch (ev)
            {
                case "scare":
                case "fear":
                case "panic":
                    threat  = Clamp01(threat + 0.6f);
                    fatigue = Clamp01(fatigue + 0.1f);
                    break;

                case "noise":
                    threat = Clamp01(threat + 0.2f);
                    break;

                case "rest":
                    fatigue = Clamp01(fatigue - 0.4f);
                    break;

                case "focus":
                    goalPull = Clamp01(goalPull + 0.3f);
                    break;
            }
        }

        var percept = new Percept(DateTimeOffset.UtcNow, tick, input);
        var drives  = new PerceptionDrives(threat, fatigue, goalPull);
        return (percept, drives);
    }

    private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
}
