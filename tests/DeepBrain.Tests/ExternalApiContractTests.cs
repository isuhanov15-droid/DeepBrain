using DeepBrain.Host.BrainLife;
using DeepBrain.Host.External;
using DeepBrain.Shared.Brain;
using Xunit;

namespace DeepBrain.Tests;

public sealed class ExternalApiContractTests
{
    [Fact]
    public void LoopbackApiCanStartWithoutToken()
    {
        var config = new ExternalApiConfig
        {
            Enable = true,
            Prefix = "http://127.0.0.1:8787/"
        };

        Assert.True(ExternalApiServer.ValidateConfiguration(config, null, out var error), error);
    }

    [Fact]
    public void NonLoopbackApiRefusesToStartWithoutToken()
    {
        var config = new ExternalApiConfig
        {
            Enable = true,
            Prefix = "http://+:8787/"
        };

        Assert.False(ExternalApiServer.ValidateConfiguration(config, null, out var error));
        Assert.Contains("bearer token", error ?? "");
    }

    [Fact]
    public void NonLoopbackApiAcceptsConfiguredToken()
    {
        var config = new ExternalApiConfig
        {
            Enable = true,
            Prefix = "http://+:8787/"
        };

        Assert.True(ExternalApiServer.ValidateConfiguration(config, "strong-test-token", out var error), error);
    }

    [Fact]
    public void ConfigurationNormalizationKeepsSafeBounds()
    {
        var llm = new LlmConfig
        {
            BaseUrl = "http://127.0.0.1:11434",
            ObserveEveryTicks = 0,
            MinObserveGapTicks = 0,
            TimeoutSeconds = 1,
            NumPredict = 5000,
            Temperature = double.NaN,
            MaxMemoryLines = 100,
            MaxRecentEvents = 100,
            MaxContextChars = 1,
            MinConfidence = double.NaN,
            JournalPath = " ",
            MaxJournalEntries = 1
        }.Normalize();
        var api = new ExternalApiConfig
        {
            Prefix = "http://127.0.0.1:8787",
            SubscriberBufferCapacity = 1
        }.Normalize();

        Assert.EndsWith("/", llm.BaseUrl);
        Assert.Equal(10, llm.ObserveEveryTicks);
        Assert.Equal(10, llm.MinObserveGapTicks);
        Assert.Equal(5, llm.TimeoutSeconds);
        Assert.Equal(1024, llm.NumPredict);
        Assert.Equal(0.1, llm.Temperature, 3);
        Assert.Equal(8, llm.MaxMemoryLines);
        Assert.Equal(12, llm.MaxRecentEvents);
        Assert.Equal(40, llm.MaxContextChars);
        Assert.Equal(0.45, llm.MinConfidence, 3);
        Assert.Equal("memory/inner-voice.jsonl", llm.JournalPath);
        Assert.Equal(10, llm.MaxJournalEntries);
        Assert.EndsWith("/", api.Prefix);
        Assert.Equal(8, api.SubscriberBufferCapacity);
    }

    [Fact]
    public void LlmDefaultsFavorCompactWarmObservations()
    {
        var llm = LlmConfig.Default.Normalize();

        Assert.Equal(128, llm.NumPredict);
        Assert.Equal(2, llm.MaxMemoryLines);
        Assert.Equal(2, llm.MaxRecentEvents);
        Assert.Equal(96, llm.MaxContextChars);
        Assert.Equal(0.45, llm.MinConfidence, 3);
        Assert.True(llm.PersistJournal);
    }

    [Fact]
    public async Task EventHubPublishesVersionedOutputEnvelope()
    {
        using var hub = new ExternalEventHub();
        using var subscription = hub.Subscribe(8);
        hub.PublishOutput(new LifeOutputDto(42, DateTimeOffset.UtcNow, "hello", "emit_message"));

        var envelope = await subscription.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ExternalContract.Version, envelope.ContractVersion);
        Assert.Equal("output", envelope.Type);
        Assert.Equal(42, envelope.Tick);
        Assert.Equal("emit_message", envelope.Payload.GetProperty("actionName").GetString());
    }
}
