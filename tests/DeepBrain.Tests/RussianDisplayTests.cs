using DeepBrain.Shared.Localization;
using Xunit;

namespace DeepBrain.Tests;

public sealed class RussianDisplayTests
{
    [Theory]
    [InlineData("rest_short", "короткий отдых")]
    [InlineData("calm_baseline", "спокойная база")]
    [InlineData("round_robin", "по очереди")]
    [InlineData("stuck_state", "застрявшее состояние")]
    public void TranslatesKnownMachineIdentifiers(string source, string expected)
    {
        Assert.Equal(expected, RussianDisplay.Token(source));
    }

    [Fact]
    public void KeepsUnknownMachineIdentifierUnchanged()
    {
        Assert.Equal("future_protocol_value", RussianDisplay.Token("future_protocol_value"));
    }

    [Fact]
    public void FormatsBooleanForOperator()
    {
        Assert.Equal("да", RussianDisplay.YesNo(true));
        Assert.Equal("нет", RussianDisplay.YesNo(false));
    }
}
