using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

/// <summary>
/// Applies the immediate safety impact of events created on the current tick.
/// Older salient events may still influence pain and appraisal, but must not
/// repeatedly charge the same safety shock on every tick of their lifetime.
/// </summary>
public static class SafetyEventIntegrator
{
    public static double Apply(
        double safety,
        IReadOnlyList<WorldEventDto> currentTickEvents,
        double dtSeconds)
    {
        var hasCalmEvent = currentTickEvents.Any(e =>
            e.Type == "calm_window" && e.Salience > 0.3);
        var hasThreatEvent = currentTickEvents.Any(e =>
            (e.Type == "threat_spike" || e.Type == "micro_threat") && e.Salience > 0.3);

        if (hasCalmEvent)
            safety += 0.03 * Math.Max(0, dtSeconds);
        if (hasThreatEvent)
            safety -= 0.05 * Math.Max(0, dtSeconds);

        return LifeMath.Clamp01(safety);
    }
}
