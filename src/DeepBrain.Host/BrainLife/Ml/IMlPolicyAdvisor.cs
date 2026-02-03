using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife.Ml;

public interface IMlPolicyAdvisor
{
    float[] Encode(StateVectorInput input);
    PolicyDecision SelectAction(float[] stateVec, IReadOnlyDictionary<string, double> heuristicScores, IReadOnlyList<string> allowedActions, MlConfig config, long tick);
    void Observe(float[] state, int actionIdx, float reward, float[] nextState, MlConfig config, long tick);
    bool TryLoad(string path);
    void TrySave(string path);
    void Reset(MlConfig config);
    MlPolicyDto BuildTelemetry(bool enabled, int inputDim, int actionCount, double avgReward200);
    void ResetCounters();
}

public readonly record struct PolicyDecision(
    string ActionName,
    double NetWeight,
    double Epsilon,
    double Entropy,
    string PolicySource,
    bool UsedMl,
    bool IllegalChoice,
    bool OverrideHeuristic
);
