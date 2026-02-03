using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed class MlPolicyAdvisorStub : IMlPolicyAdvisor
{
    public float[] Encode(StateVectorInput input) => Array.Empty<float>();

    public PolicyDecision SelectAction(float[] stateVec, IReadOnlyDictionary<string, double> heuristicScores, IReadOnlyList<string> allowedActions, MlConfig config, long tick)
    {
        var action = heuristicScores
            .Where(kv => allowedActions.Count == 0 || allowedActions.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault() ?? (allowedActions.Count > 0 ? allowedActions[0] : ActionCatalog.Actions[0]);
        return new PolicyDecision(action, 0, 0, 0, "heuristic", false, false, false);
    }

    public void Observe(float[] state, int actionIdx, float reward, float[] nextState, MlConfig config, long tick) { }

    public bool TryLoad(string path) => false;
    public void TrySave(string path) { }
    public void Reset(MlConfig config) { }

    public MlPolicyDto BuildTelemetry(bool enabled, int inputDim, int actionCount, double avgReward200)
    {
        return new MlPolicyDto(
            Enabled: false,
            InputDim: inputDim,
            ActionCount: actionCount,
            BufferSize: 0,
            BufferCapacity: 0,
            Epsilon: 0,
            NetWeight: 0,
            LastLoss: 0,
            AvgLoss100: 0,
            AvgReward200: avgReward200,
            Entropy: 0,
            TrainSteps: 0,
            NanSkips: 0,
            IllegalChoiceCount: 0,
            OverrideCount: 0,
            PolicySource: "heuristic"
        );
    }

    public void ResetCounters() { }
}
