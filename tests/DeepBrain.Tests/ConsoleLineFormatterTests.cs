using DeepBrain.Host.ConsoleUi;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V2;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class ConsoleLineFormatterTests
{
    [Fact]
    public void TruncatesToRequestedWidth()
    {
        var line = ConsoleLineFormatter.FitToWidth("tick=100 ep=2 act=explore_signal reward=0.123 loop=repeat eps=0.500", 24);

        Assert.True(line.Length <= 24);
        Assert.EndsWith("...", line);
    }

    [Fact]
    public void FormatsStateSummaryCompactly()
    {
        var state = new LifeStateDto(
            42,
            DateTimeOffset.UtcNow,
            new HomeostasisDto(0.5, 0.2, 0.3, 0.1, 0.8),
            new InstinctsDto(0.1, 0.2, 0.3, 0.4, 0.5),
            new AffectDto("calm", 0.2, 0.3),
            "rest_short",
            0.125,
            Reward: new RewardDto(0.02, 0.0, 0.0, -0.01, 0.0, 0.01),
            Episode: new EpisodeInfoDto(2, 40, 1200, "none"),
            Scenario: new ScenarioInfoDto("calm_baseline", "round_robin", 0),
            Ml: new MlPolicyDto(
                Enabled: true,
                InputDim: 0,
                ActionCount: 0,
                BufferSize: 0,
                BufferCapacity: 0,
                Epsilon: 0.1,
                NetWeight: 0.2,
                LastLoss: 0.0,
                AvgLoss100: 0.0,
                AvgReward200: 0.0,
                AvgQ: 0.5,
                Entropy: 0.0,
                TrainSteps: 0,
                NanSkips: 0,
                IllegalChoiceCount: 0,
                OverrideCount: 0,
                InvalidActionFallbackCount: 0,
                PolicySource: "stub",
                CoreAvailable: false,
                BackendKind: "stub",
                RemoteConnected: false,
                RttMs: 0.0,
                LastRemoteError: null,
                ReasonIfDisabled: null,
                MlMode: "evaluation",
                TrainEnabled: false,
                TrainingEpisodeCount: 0,
                EvalEpisodeCount: 0),
            Decision: new DecisionDto("rest_short", "internal", "energy_conservation", "ok", "heuristic"),
            LoopInfo: new LoopInfoDto(false, 0, 0, 0.0, 0.0, "none", 0)
        );

        Assert.StartsWith("тик=42 эпизод=2 действие=короткий отдых", ConsoleLineFormatter.FormatTickLine(state));
        Assert.Equal("сценарий=спокойная база режим=по очереди индекс=0", ConsoleLineFormatter.FormatScenarioLine(state));
    }
}
