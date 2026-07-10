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

        Assert.Equal("решение действие=короткий отдых тип=внутреннее причина=сбережение энергии маска=норма", line);
        Assert.DoesNotContain("{", line);
    }

    [Fact]
    public void KeepsRewardLineReadable()
    {
        var trace = new TraceDto(11, "reward", "награда всего=+0.03 гомео=+0.02 исслед=+0.00 соц=+0.00 петля=-0.01 недоп=+0.00");

        var line = TraceFormatter.FormatCompact(trace);

        Assert.Equal("награда всего=+0.03 гомео=+0.02 исслед=+0.00 соц=+0.00 петля=-0.01 недоп=+0.00", line);
        Assert.DoesNotContain("\\u002B", line);
    }
}
