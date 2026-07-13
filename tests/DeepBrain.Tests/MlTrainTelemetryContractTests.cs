using System.Text.Json;
using DeepBrain.Shared.MlBridge;
using Xunit;

namespace DeepBrain.Tests;

public sealed class MlTrainTelemetryContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void ExtendedMlHostResponseCarriesTrainingTelemetry()
    {
        const string json = """
        {
          "ok": true,
          "loss": 0.00042,
          "avgQ": 0.71,
          "trainSteps": 24795,
          "epsilon": 0.05,
          "invalidActions": 0,
          "reason": null,
          "trained": true,
          "gradNorm": 0.37,
          "bufferSize": 20000
        }
        """;

        var response = JsonSerializer.Deserialize<MlTrainResponse>(json, JsonOptions);

        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.True(response.Trained);
        Assert.Equal(0.00042, response.Loss, 8);
        Assert.Equal(0.37, response.GradNorm, 8);
        Assert.Equal(24_795, response.TrainSteps);
        Assert.Equal(20_000, response.BufferSize);
        Assert.Null(response.Reason);
    }

    [Fact]
    public void LegacyMlHostResponseRemainsReadable()
    {
        const string json = """
        {
          "ok": true,
          "loss": 0.00042,
          "avgQ": 0.71,
          "trainSteps": 24795,
          "epsilon": 0.05,
          "invalidActions": 0,
          "reason": null
        }
        """;

        var response = JsonSerializer.Deserialize<MlTrainResponse>(json, JsonOptions);

        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.False(response.Trained);
        Assert.Equal(0.00042, response.Loss, 8);
        Assert.Equal(0, response.GradNorm);
        Assert.Equal(24_795, response.TrainSteps);
        Assert.Equal(0, response.BufferSize);
    }
}
