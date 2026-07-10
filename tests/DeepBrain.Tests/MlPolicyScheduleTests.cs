using DeepBrain.Host.BrainLife;
using DeepBrain.Host.BrainLife.Ml;
using Xunit;

namespace DeepBrain.Tests;

public sealed class MlPolicyScheduleTests
{
    [Fact]
    public void LoadedTrainingStepsPreserveMatureNetworkWeight()
    {
        var config = BrainConfig.Default.Ml with
        {
            NetWeightMax = 0.6,
            NetWeightWarmup = 5000
        };

        var weight = MlPolicySchedule.ComputeNetWeight(config, bufferSize: 0, trainSteps: 18_000);

        Assert.Equal(0.6, weight, precision: 6);
    }

    [Fact]
    public void ColdPolicyHasNoInfluenceAndKeepsExploration()
    {
        var config = BrainConfig.Default.Ml with { BatchSize = 256 };

        Assert.Equal(0.0, MlPolicySchedule.ComputeNetWeight(config, 0, 0));
        Assert.False(MlPolicySchedule.CanDecayExploration(config, bufferSize: 255, trainSteps: 0));
        Assert.True(MlPolicySchedule.CanDecayExploration(config, bufferSize: 256, trainSteps: 0));
    }

    [Fact]
    public void EvaluationModeForcesExplorationToZero()
    {
        var config = BrainConfig.Default.Ml with
        {
            EpsilonStart = 0,
            EpsilonEnd = 0,
            EpsilonMin = 0,
            EpsilonDecay = 0
        };

        var epsilon = MlPolicySchedule.NextEpsilon(config, current: 0.42, bufferSize: 1000, trainSteps: 100);

        Assert.Equal(0.0, epsilon);
    }
}
