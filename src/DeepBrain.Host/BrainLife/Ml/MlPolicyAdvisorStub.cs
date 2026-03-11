using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife.Ml;

public sealed class MlPolicyAdvisorStub : IMlPolicyAdvisor
{
    public float[] Encode(StateVectorInput input) => Array.Empty<float>();

    public PolicyDecision SelectAction(float[] stateVec, IReadOnlyDictionary<string, double> heuristicScores, IReadOnlyList<string> allowedActions, float[] actionMask, MlConfig config, long tick)
    {
        var action = heuristicScores
            .Where(kv => allowedActions.Count == 0 || allowedActions.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault() ?? (allowedActions.Count > 0 ? allowedActions[0] : ActionCatalog.Actions[0]);
        return new PolicyDecision(action, 0, 0, 0, "heuristic", false, false, false);
    }

    public void Observe(float[] state, int actionIdx, float reward, float[] nextState, bool done, float[] nextActionMask, MlConfig config, long tick) { }

    public bool TryLoad(string path, out int episodeId)
    {
        episodeId = 0;
        return false;
    }

    public void TrySave(string path, int episodeId) { }
    public void Reset(MlConfig config) { }

    public MlPolicyDto BuildTelemetry(bool enabled, bool coreAvailable, int inputDim, int actionCount, double avgReward200, string? reasonIfDisabled, string mlMode, bool trainEnabled, int trainingEpisodeCount, int evalEpisodeCount)
    {
        return new MlPolicyDto(
            Enabled: false,
            CoreAvailable: coreAvailable,
            InputDim: inputDim,
            ActionCount: actionCount,
            BufferSize: 0,
            BufferCapacity: 0,
            Epsilon: 0,
            NetWeight: 0,
            LastLoss: 0,
            AvgLoss100: 0,
            AvgReward200: avgReward200,
            AvgQ: 0,
            Entropy: 0,
            TrainSteps: 0,
            NanSkips: 0,
            IllegalChoiceCount: 0,
            OverrideCount: 0,
            InvalidActionFallbackCount: 0,
            PolicySource: "heuristic",
            BackendKind: "stub",
            RemoteConnected: false,
            RttMs: 0,
            LastRemoteError: null,
            ReasonIfDisabled: reasonIfDisabled,
            MlMode: mlMode,
            TrainEnabled: trainEnabled,
            TrainingEpisodeCount: trainingEpisodeCount,
            EvalEpisodeCount: evalEpisodeCount
        );
    }

    public void ResetCounters() { }
    public bool TryConnectRemote() => false;
    public void DisconnectRemote() { }
}
