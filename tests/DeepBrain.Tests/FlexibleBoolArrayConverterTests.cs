using System.Text.Json;
using DeepBrain.Shared.MlBridge;
using Xunit;

namespace DeepBrain.Tests;

public sealed class FlexibleBoolArrayConverterTests
{
    [Fact]
    public void ParsesMixedBoolArrayValues()
    {
        var json = """
        {
          "state": [0.0],
          "actionMask": [1, 0, "1", "0", "true", "false"],
          "inputDim": 1,
          "actionCount": 6
        }
        """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var req = JsonSerializer.Deserialize<MlInferRequest>(json, options);
        Assert.NotNull(req);
        Assert.Equal(new[] { true, false, true, false, true, false }, req!.ActionMask);
    }
}
