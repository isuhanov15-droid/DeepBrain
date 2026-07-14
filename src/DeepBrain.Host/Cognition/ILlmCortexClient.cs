using DeepBrain.Host.BrainLife;

namespace DeepBrain.Host.Cognition;

public interface ILlmCortexClient : IAsyncDisposable
{
    Task<LlmCortexResponse> ObserveAsync(
        CognitiveFrame frame,
        LlmConfig config,
        CancellationToken cancellationToken);
}
