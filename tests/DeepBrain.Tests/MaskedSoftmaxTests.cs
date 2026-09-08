using DeepBrain.Host.BrainLife.Ml;
using Xunit;
namespace DeepBrain.Tests;
public sealed class MaskedSoftmaxTests
{
    [Fact]
    public void ForbiddenLargeQDoesNotEraseLegalProbabilities()
    {
        var p = MlMath.Softmax(new[] { 2000d, 2d, 1d }, new[] { 0f, 1f, 1f });
        Assert.Equal(0, p[0]);
        Assert.Equal(1, p.Sum(), 12);
        Assert.True(p[1] > p[2]);
        Assert.InRange(p[1], 0.73, 0.74);
    }
    [Fact]
    public void NoLegalActionsProduceNoProbabilityMass()
    {
        Assert.All(MlMath.Softmax(new[] { 3d, 2d }, new[] { 0f, 0f }), p => Assert.Equal(0, p));
        Assert.Equal(0, MlMath.ComputeEntropy(new[] { 1d }));
    }
}
