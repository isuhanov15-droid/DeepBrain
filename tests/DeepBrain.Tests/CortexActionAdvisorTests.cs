using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Host.Cognition;
using Xunit;

namespace DeepBrain.Tests;

public sealed class CortexActionAdvisorTests
{
    private static readonly LlmConfig Config = new()
    {
        Enable = true, ActionAdviceEnabled = true, ActionAdviceMaxAgeTicks = 100
    };
    private static CognitiveInsightEnvelope Advice(long id = 1, string action = "explore_signal", double confidence = 0.8) =>
        new(CognitiveContract.Version, id, 100, 150, DateTimeOffset.UtcNow, "test", "test", "test",
            TimeSpan.Zero, new CognitiveInsight("ok", "Исследую", "", "low", confidence) { SuggestedAction = action })
        { SourceContextId = "run:episode:awake" };
    private static Dictionary<string, double> Scores() => new()
    {
        ["rest_short"] = 0.5, ["explore_signal"] = 0.45, ["emit_message"] = double.NegativeInfinity
    };

    [Theory]
    [InlineData(200, "applied")]
    [InlineData(201, "stale")]
    [InlineData(99, "stale")]
    public void ExpiryUsesSourceTickIncludingGenerationDelay(long tick, string status)
    {
        var advisor = new CortexActionAdvisor(); advisor.Accept(Advice());
        var scores = Scores();
        var result = advisor.Apply(Config, tick, "run:episode:awake", false, 1, 0, scores);
        Assert.Equal(status, result.Status);
        Assert.Equal(status == "applied" ? 0.57 : 0.45, scores["explore_signal"], 8);
    }

    [Theory]
    [InlineData(true, "run:episode:awake", 1, 0, "sleeping")]
    [InlineData(false, "run:episode:new-awake", 1, 0, "context_changed")]
    [InlineData(false, "new-run:episode:awake", 1, 0, "context_changed")]
    [InlineData(false, "run:episode:awake", 0.2, 0, "urgent_recovery")]
    [InlineData(false, "run:episode:awake", 1, 0.8, "urgent_recovery")]
    public void ContextAndRecoveryGuardsLeaveScoresUntouched(bool asleep, string context, double safety, double pain, string status)
    {
        var advisor = new CortexActionAdvisor(); advisor.Accept(Advice());
        var scores = Scores();
        Assert.Equal(status, advisor.Apply(Config, 155, context, asleep, safety, pain, scores).Status);
        Assert.Equal(Scores(), scores);
    }

    [Theory]
    [InlineData("emit_message", 0.9, "unavailable")]
    [InlineData("sleep", 0.9, "unavailable")]
    [InlineData("explore_signal", 0.5, "low_confidence")]
    [InlineData("none", 0.9, "none")]
    public void UnavailableAndUncertainAdviceCannotAffectPolicy(string action, double confidence, string status)
    {
        var advisor = new CortexActionAdvisor(); advisor.Accept(Advice(action: action, confidence: confidence));
        var scores = Scores();
        Assert.Equal(status, advisor.Apply(Config, 155, "run:episode:awake", false, 1, 0, scores).Status);
        Assert.Equal(Scores(), scores);
    }

    [Fact]
    public void NewerAbstentionReplacesAdviceAndOldReplyCannotReviveIt()
    {
        var advisor = new CortexActionAdvisor();
        advisor.Accept(Advice()); advisor.Accept(Advice(2, "none")); advisor.Accept(Advice());
        Assert.Equal("none", advisor.Apply(Config, 155, "run:episode:awake", false, 1, 0, Scores()).Status);
        Assert.Equal("waiting", new CortexActionAdvisor().Apply(Config, 155, "run:episode:awake", false, 1, 0, Scores()).Status);
    }

    [Fact]
    public void BiasIsBoundedAndDisabledModeIsUnchanged()
    {
        var advisor = new CortexActionAdvisor(); advisor.Accept(Advice(confidence: 1));
        var scores = Scores();
        var result = advisor.Apply(Config with { ActionAdviceMaxBias = 100 }, 155, "run:episode:awake", false, 1, 0, scores);
        Assert.Equal(0.25, result.ScoreBias);
        scores = Scores();
        Assert.Equal("disabled", advisor.Apply(Config with { ActionAdviceEnabled = false }, 155, "run:episode:awake", false, 1, 0, scores).Status);
        Assert.Equal(Scores(), scores);
    }

    [Fact]
    public void AdviceChangesActualBlendedPolicyWithoutChangingNetworkShape()
    {
        var config = BrainConfig.Default.Ml with
        {
            Enable = true, Backend = "remote", EpsilonStart = 0, EpsilonEnd = 0, EpsilonMin = 0,
            EpsilonDecay = 0, NetWeightMax = 0.6, NetWeightWarmup = 5000
        };
        var ml = new MlPolicyAdvisor(config, _ => { }, new FixedBackend());
        var scores = Scores(); var allowed = new[] { "rest_short", "explore_signal" };
        var mask = ActionCatalog.Actions.Select(a => allowed.Contains(a) ? 1f : 0f).ToArray();
        var state = new float[StateVectorizer.InputDim];
        Assert.Equal("rest_short", ml.SelectAction(state, scores, allowed, mask, config, 155).ActionName);
        var advisor = new CortexActionAdvisor(); advisor.Accept(Advice());
        var advice = advisor.Apply(Config, 155, "run:episode:awake", false, 1, 0, scores);
        var decision = ml.SelectAction(state, scores, allowed, mask, config, 155);
        Assert.Equal("explore_signal", decision.ActionName);
        Assert.Equal(0.6, decision.NetWeight, 6);
        Assert.True(CortexActionAdvisor.WithSelection(advice, decision.ActionName).MatchesSelection);
        Assert.False(CortexActionAdvisor.WithSelection(advice, "rest_short").MatchesSelection);
        Assert.Equal(38, StateVectorizer.InputDim); Assert.Equal(8, ActionCatalog.Count);
    }

    [Theory]
    [InlineData("explore_signal", true)]
    [InlineData("none", true)]
    [InlineData("sleep", false)]
    [InlineData("execute_command", false)]
    public void ExtendedContractOnlyAllowsKnownActions(string action, bool valid)
    {
        var json = JsonSerializer.Serialize(new CognitiveInsight("ok", "", "", "low", 0.8) { SuggestedAction = action });
        Assert.Equal(valid, CognitiveInsight.TryParseStrict(json, out var parsed, out _));
        if (valid) Assert.Equal(action, parsed!.SuggestedAction);
    }

    [Fact]
    public void OldJournalRemainsReadableWithNoExecutableAdvice()
    {
        const string json = """{"interpretation":"ok","inner_speech":"Исследую","memory_question":"","risk_level":"low","confidence":0.8}""";
        Assert.True(CognitiveInsight.TryParseStrict(json, out var insight, out _));
        Assert.Equal("none", insight!.SuggestedAction);
        Assert.Equal("none", JsonSerializer.Deserialize<CognitiveInsight>(json)!.SuggestedAction);
        Assert.False(CognitiveInsight.TryParseStrict(json[..^1] + ",\"confidence\":0.9}", out _, out _));
    }

    private sealed class FixedBackend : IBrainMlBackend
    {
        public bool IsAvailable => true; public string Kind => "remote"; public bool IsConnected => true;
        public string? LastError => null; public double LastRttMs => 0;
        public int InputDim => StateVectorizer.InputDim; public int ActionCount => ActionCatalog.Count;
        public int BufferSize => 0; public int BufferCapacity => 20_000; public double LastLoss => 0;
        public double AvgLoss100 => 0; public long TrainSteps => 93600; public double AvgQ => 0;
        public Task<MlInferResult> InferAsync(float[] state, float[] mask, MlConfig config, CancellationToken ct) =>
            Task.FromResult(new MlInferResult(true, new double[ActionCatalog.Count], new float[ActionCatalog.Count], 0, 0));
        public Task<MlTrainResult> TrainAsync(Transition transition, MlConfig config, long tick, CancellationToken ct) =>
            Task.FromResult(new MlTrainResult(false, 0, 0, TrainSteps, false));
        public bool TryLoad(string path, out int episodeId) { episodeId = 0; return true; }
        public void TrySave(string path, int episodeId) { }
        public void Reset(MlConfig config) { } public void ResetCounters() { } public void UpdateConfig(MlConfig config) { }
        public bool TryConnect() => true; public void Disconnect() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
