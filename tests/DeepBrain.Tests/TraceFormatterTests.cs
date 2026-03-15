using System.Text.Json;
using DeepBrain.Shared.Trace;
using Xunit;

namespace DeepBrain.Tests;

public sealed class TraceFormatterTests
{
    [Fact]
    public void FormatsDecisionWithoutJsonNoise()
    {
        var trace = new TraceDto(10, "decision", JsonSerializer.SerializeToElement(new
        {
            actionName = "rest_short",
            kind = "internal",
            reason = "energy_conservation",
            mask = "ok"
        }));

        var line = TraceFormatter.FormatCompact(trace);

        Assert.Equal("decision act=rest_short kind=internal reason=energy_conservation mask=ok", line);
        Assert.DoesNotContain("{", line);
    }

    [Fact]
    public void KeepsRewardLineReadable()
    {
        var trace = new TraceDto(11, "reward", "reward tot=+0.03 h=+0.02 x=+0.00 s=+0.00 lp=-0.01 ia=+0.00");

        var line = TraceFormatter.FormatCompact(trace);

        Assert.Equal("reward tot=+0.03 h=+0.02 x=+0.00 s=+0.00 lp=-0.01 ia=+0.00", line);
        Assert.DoesNotContain("\\u002B", line);
    }
}
