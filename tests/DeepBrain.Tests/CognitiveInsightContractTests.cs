using DeepBrain.Host.Cognition;
using Xunit;

namespace DeepBrain.Tests;

public sealed class CognitiveInsightContractTests
{
    [Fact]
    public void StrictContractAcceptsCompleteBoundedResponse()
    {
        const string json = """
        {
          "interpretation": "Состояние устойчиво",
          "inner_speech": "Я могу спокойно наблюдать",
          "memory_question": "Что помогало раньше?",
          "risk_level": "low",
          "confidence": 0.82
        }
        """;

        var ok = CognitiveInsight.TryParseStrict(json, out var insight, out var error);

        Assert.True(ok, error);
        Assert.NotNull(insight);
        Assert.Equal("low", insight.RiskLevel);
        Assert.Equal(0.82, insight.Confidence, 3);
    }

    [Fact]
    public void StrictContractRejectsUnknownProperty()
    {
        const string json = """
        {
          "interpretation": "ok",
          "inner_speech": "",
          "memory_question": "",
          "risk_level": "low",
          "confidence": 0.5,
          "action": "open_door"
        }
        """;

        Assert.False(CognitiveInsight.TryParseStrict(json, out _, out var error));
        Assert.Contains("unknown=[action]", error ?? "");
    }

    [Fact]
    public void StrictContractRejectsMissingProperty()
    {
        const string json = """
        {
          "interpretation": "ok",
          "inner_speech": "",
          "risk_level": "low",
          "confidence": 0.5
        }
        """;

        Assert.False(CognitiveInsight.TryParseStrict(json, out _, out var error));
        Assert.Contains("memory_question", error ?? "");
    }

    [Theory]
    [InlineData("critical", 0.5)]
    [InlineData("low", -0.1)]
    [InlineData("low", 1.1)]
    public void StrictContractRejectsInvalidRiskOrConfidence(string risk, double confidence)
    {
        var json = $$"""
        {
          "interpretation": "ok",
          "inner_speech": "",
          "memory_question": "",
          "risk_level": "{{risk}}",
          "confidence": {{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
        }
        """;

        Assert.False(CognitiveInsight.TryParseStrict(json, out _, out _));
    }

    [Fact]
    public void StrictContractRejectsOversizedInterpretation()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            interpretation = new string('x', CognitiveContract.InterpretationMaxLength + 1),
            inner_speech = "",
            memory_question = "",
            risk_level = "low",
            confidence = 0.5
        });

        Assert.False(CognitiveInsight.TryParseStrict(json, out _, out var error));
        Assert.Contains($"[1,{CognitiveContract.InterpretationMaxLength}]", error ?? "");
    }
}
