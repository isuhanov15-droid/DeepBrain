using DeepBrain.Host.BrainLife;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.Cognition;

/// <summary>Ephemeral advice: never restored from the cognitive journal.</summary>
public sealed class CortexActionAdvisor
{
    private readonly object _gate = new();
    private CognitiveInsightEnvelope? _latest;

    public void Accept(CognitiveInsightEnvelope envelope)
    {
        lock (_gate)
        {
            if (_latest is null || envelope.FrameId > _latest.FrameId)
                _latest = envelope;
        }
    }

    // Mutates only this tick's already-masked scores, before the existing ML blend.
    public CortexAdviceDto Apply(LlmConfig settings, long tick, string contextId, bool sleeping,
        double safety, double pain, IDictionary<string, double> scores)
    {
        var config = settings.Normalize();
        CognitiveInsightEnvelope? envelope;
        lock (_gate) envelope = _latest;
        var insight = envelope?.Insight;
        var age = envelope is null ? (long?)null : tick - envelope.FrameTick;
        var result = new CortexAdviceDto("waiting", insight?.SuggestedAction ?? "none",
            insight?.Confidence ?? 0, 0, envelope?.FrameId, envelope?.FrameTick, age);
        if (!config.Enable || !config.ActionAdviceEnabled) return result with { Status = "disabled" };
        if (sleeping) return result with { Status = "sleeping" };
        if (envelope is null) return result;
        if (age < 0 || age > config.ActionAdviceMaxAgeTicks) return result with { Status = "stale" };
        if (string.IsNullOrEmpty(contextId) || envelope.SourceContextId != contextId)
            return result with { Status = "context_changed" };
        if (insight!.SuggestedAction == "none") return result with { Status = "none" };
        if (!double.IsFinite(insight.Confidence) || insight.Confidence < config.ActionAdviceMinConfidence)
            return result with { Status = "low_confidence" };
        // A delayed language-model recommendation cannot compete with urgent recovery.
        if (!double.IsFinite(safety) || !double.IsFinite(pain) || safety < 0.35 || pain > 0.65)
            return result with { Status = "urgent_recovery" };
        if (ActionCatalog.IndexOf(insight.SuggestedAction) < 0 ||
            !scores.TryGetValue(insight.SuggestedAction, out var score) || !double.IsFinite(score))
            return result with { Status = "unavailable" };
        var bias = config.ActionAdviceMaxBias * insight.Confidence;
        if (bias <= 0) return result with { Status = "disabled" };
        scores[insight.SuggestedAction] = score + bias;
        return result with { Status = "applied", ScoreBias = bias };
    }

    public static CortexAdviceDto WithSelection(CortexAdviceDto advice, string selectedAction) => advice with
    {
        SelectedAction = selectedAction,
        MatchesSelection = advice.SuggestedAction != "none" && advice.SuggestedAction == selectedAction
    };
}
