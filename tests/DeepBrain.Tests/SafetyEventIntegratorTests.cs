using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.BrainDtos.V4;
using Xunit;

namespace DeepBrain.Tests;

public sealed class SafetyEventIntegratorTests
{
    [Fact]
    public void ThreatShockIsChargedOnlyWhenEventIsNew()
    {
        var threat = new WorldEventDto(
            Tick: 10,
            Type: "micro_threat",
            Severity: 0.4,
            Payload: "test",
            AgeSeconds: 0,
            Salience: 0.4,
            Consumed: false);

        var afterArrival = SafetyEventIntegrator.Apply(0.70, new[] { threat }, 0.2);
        var afterFollowingTick = SafetyEventIntegrator.Apply(afterArrival, Array.Empty<WorldEventDto>(), 0.2);

        Assert.Equal(0.69, afterArrival, 6);
        Assert.Equal(afterArrival, afterFollowingTick, 6);
    }
}
