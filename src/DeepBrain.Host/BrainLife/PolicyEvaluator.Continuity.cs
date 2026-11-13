// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class PolicyEvaluator
{
    private sealed class ContinuityState
    {
        [JsonRequired] public Queue<EpisodeReport> Training { get; set; } = default!;
        [JsonRequired] public Queue<EpisodeReport> Evaluation { get; set; } = default!;
        [JsonRequired] public int Window { get; set; } = default!;
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            Training = _training,
            Evaluation = _evaluation,
            Window = _window
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _training.Clear();
        foreach (var item in v.Training) _training.Enqueue(item);
        _evaluation.Clear();
        foreach (var item in v.Evaluation) _evaluation.Enqueue(item);
        _window = v.Window;
    }
}
