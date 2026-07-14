using System.Net;
using System.Text;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Host.Cognition;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V2;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class LlmCortexTelemetryTests
{
    [Fact]
    public async Task OllamaRequestIsCompactAndReportsGenerationMetrics()
    {
        const string insightJson = """
        {
          "interpretation": "Состояние устойчиво",
          "inner_speech": "Продолжаю наблюдение",
          "memory_question": "Что помогало раньше?",
          "risk_level": "low",
          "confidence": 0.9
        }
        """;
        var ollamaJson = JsonSerializer.Serialize(new
        {
            done_reason = "stop",
            total_duration = 2_000_000_000L,
            load_duration = 100_000_000L,
            prompt_eval_count = 61,
            prompt_eval_duration = 500_000_000L,
            eval_count = 43,
            eval_duration = 1_000_000_000L,
            message = new { role = "assistant", content = insightJson }
        });
        var handler = new CapturingHandler(ollamaJson);
        using var httpClient = new HttpClient(handler);
        await using var client = new OllamaCortexClient(httpClient);
        var config = new LlmConfig
        {
            NumPredict = 128,
            MaxRecentEvents = 2,
            MaxMemoryLines = 2,
            MaxContextChars = 60
        }.Normalize();
        var frame = new CognitiveFrame(
            CognitiveContract.Version,
            7,
            123,
            DateTimeOffset.UtcNow,
            "mixed_adaptive",
            "calm",
            0.987654,
            0,
            0.212345,
            "energy_conservation",
            "focus_widen",
            0.012345,
            Enumerable.Range(0, 10).Select(index => $"event-{index}-" + new string('e', 200)).ToArray(),
            Enumerable.Range(0, 6).Select(index => $"memory-{index}-" + new string('m', 300)).ToArray());

        var response = await client.ObserveAsync(frame, config, CancellationToken.None);

        Assert.True(response.Success, response.Error);
        var metrics = Assert.IsType<LlmGenerationMetrics>(response.Metrics);
        Assert.Equal(61L, metrics.PromptTokens!.Value);
        Assert.Equal(43L, metrics.OutputTokens!.Value);
        Assert.Equal(2d, metrics.TotalSeconds!.Value, 3);
        Assert.Equal(122d, metrics.PromptTokensPerSecond!.Value, 3);
        Assert.Equal(43d, metrics.OutputTokensPerSecond!.Value, 3);
        Assert.NotNull(handler.RequestBody);
        Assert.Equal(Encoding.UTF8.GetByteCount(handler.RequestBody), metrics.RequestBytes);
        Assert.True(metrics.PromptCharacters > metrics.DataCharacters);
        Assert.True(metrics.RequestBytes < 5_000, $"request bytes={metrics.RequestBytes}");

        using var requestDocument = JsonDocument.Parse(handler.RequestBody);
        var request = requestDocument.RootElement;
        Assert.False(request.GetProperty("think").GetBoolean());
        Assert.Equal(128, request.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.Equal(
            CognitiveContract.InterpretationMaxLength,
            request.GetProperty("format").GetProperty("properties")
                .GetProperty("interpretation").GetProperty("maxLength").GetInt32());

        var messages = request.GetProperty("messages");
        Assert.Contains("Обязательны ровно пять ключей", messages[0].GetProperty("content").GetString());
        var userContent = messages[1].GetProperty("content").GetString() ?? "";
        Assert.StartsWith("DATA", userContent);
        var dataOffset = userContent.IndexOf('\n') + 1;
        Assert.True(dataOffset > 0);
        var data = userContent[dataOffset..];
        Assert.Equal(data.Length, metrics.DataCharacters);
        Assert.Equal(
            (messages[0].GetProperty("content").GetString() ?? "").Length + userContent.Length,
            metrics.PromptCharacters);
        Assert.True(data.Length < 600, $"data chars={data.Length}");
        Assert.DoesNotContain("contractVersion", data);
        Assert.DoesNotContain("frameId", data);
        Assert.DoesNotContain("timestamp", data);
        Assert.Contains("state|scenario=mixed_adaptive", data);
        Assert.Contains("|safety=0.988|", data);
        Assert.Contains("\nevents|event-8-", data);
        Assert.Contains("|event-9-", data);
        Assert.DoesNotContain("event-7-", data);
        Assert.Contains("\nmemory|memory-0-", data);
        Assert.Contains("|memory-1-", data);
        Assert.DoesNotContain("memory-2-", data);
    }

    [Fact]
    public async Task OrchestratorCompactsTypedEventsAndOmitsPayload()
    {
        var client = new CapturingFrameClient();
        await using var orchestrator = new LlmCortexOrchestrator(
            () => new LlmConfig
            {
                Enable = true,
                MaxRecentEvents = 2,
                MaxContextChars = 96
            },
            () => Array.Empty<string>(),
            _ => { },
            _ => { },
            client);
        orchestrator.Start(CancellationToken.None);
        var state = BuildState(100) with
        {
            Scenario = new ScenarioInfoDto("mixed_adaptive", "round_robin", 5),
            RecentEvents = new WorldEventDto[]
            {
                new(90, "old_event", 0.1, "старый payload", 2, 0.2, true),
                new(95, "calm_window", 0.31, "безопасное окно", 1, 0.44, false),
                new(99, "micro_threat", 0.39, "небольшой риск", 0.2, 0.47, true)
            }
        };
        orchestrator.PublishState(state);

        Assert.True(orchestrator.TryQueueObservation("test", out _));

        var frame = Assert.IsType<CognitiveFrame>(client.Frame);
        Assert.Equal("mixed_adaptive", frame.Scenario);
        Assert.Equal(
            new[] { "calm_window:0.31:0.44:0", "micro_threat:0.39:0.47:1" },
            frame.RecentEvents);
        var events = string.Join("|", frame.RecentEvents);
        Assert.DoesNotContain("payload", events, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("безопасное", events, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('{', events);
    }

    [Fact]
    public async Task OrchestratorExposesLastLatencyFrameAgeAndOllamaMetrics()
    {
        var metrics = new LlmGenerationMetrics(
            1024,
            "stop",
            61,
            43,
            2,
            0.1,
            0.5,
            1);
        var response = new LlmCortexResponse(
            true,
            new CognitiveInsight("Устойчиво", "Наблюдаю", "Что помогало?", "low", 0.9),
            TimeSpan.FromSeconds(2.5),
            null,
            metrics);
        var client = new StubClient(response);
        CognitiveInsightEnvelope? published = null;
        await using var orchestrator = new LlmCortexOrchestrator(
            () => new LlmConfig { Enable = true },
            () => Array.Empty<string>(),
            value => published = value,
            _ => { },
            client);
        orchestrator.Start(CancellationToken.None);
        orchestrator.PublishState(BuildState(100));

        Assert.True(orchestrator.TryQueueObservation("test", out var frameId));

        var status = orchestrator.GetStatus();
        Assert.Equal(1L, frameId);
        Assert.Equal(1L, status.Completed);
        Assert.Equal(0L, status.Failed);
        Assert.False(status.InFlight);
        Assert.Equal(2.5d, status.LastLatencySeconds!.Value, 3);
        Assert.Equal(0L, status.LastFrameAgeTicks);
        Assert.Same(metrics, status.LastMetrics);
        Assert.NotNull(published);
        Assert.Same(metrics, published!.Metrics);
        Assert.Contains("json=1024Б", orchestrator.GetStatusLine());
        Assert.Contains("prompt=61", orchestrator.GetStatusLine());
    }

    [Fact]
    public async Task InFlightStatusShowsElapsedTimeAndLiveFrameAge()
    {
        var client = new DeferredClient();
        await using var orchestrator = new LlmCortexOrchestrator(
            () => new LlmConfig { Enable = true },
            () => Array.Empty<string>(),
            _ => { },
            _ => { },
            client);
        orchestrator.Start(CancellationToken.None);
        orchestrator.PublishState(BuildState(100));
        Assert.True(orchestrator.TryQueueObservation("test", out _));
        await Task.Delay(20);
        orchestrator.PublishState(BuildState(115));

        var statusWhileRunning = orchestrator.GetStatus();
        client.Complete(new LlmCortexResponse(
            true,
            new CognitiveInsight("Устойчиво", "Наблюдаю", "Что помогало?", "low", 0.9),
            TimeSpan.FromMilliseconds(25),
            null));
        for (var i = 0; i < 100 && orchestrator.GetStatus().InFlight; i++)
            await Task.Delay(5);

        Assert.True(statusWhileRunning.InFlight);
        Assert.Equal(100L, statusWhileRunning.InFlightFrameTick);
        Assert.Equal(15L, statusWhileRunning.InFlightAgeTicks);
        Assert.NotNull(statusWhileRunning.InFlightStartedAt);
        Assert.True(statusWhileRunning.InFlightSeconds > 0);
        Assert.False(orchestrator.GetStatus().InFlight);
    }

    private static LifeStateDto BuildState(long tick) => new(
        tick,
        DateTimeOffset.UtcNow,
        new HomeostasisDto(0.5, 0.2, 0.3, 0.1, 0.9),
        new InstinctsDto(0.1, 0.2, 0.3, 0.4, 0.5),
        new AffectDto("calm", 0.2, 0.3),
        "focus_widen",
        0.01,
        DominantDrive: "energy_conservation");

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubClient(LlmCortexResponse response) : ILlmCortexClient
    {
        public Task<LlmCortexResponse> ObserveAsync(
            CognitiveFrame frame,
            LlmConfig config,
            CancellationToken cancellationToken) => Task.FromResult(response);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CapturingFrameClient : ILlmCortexClient
    {
        public CognitiveFrame? Frame { get; private set; }

        public Task<LlmCortexResponse> ObserveAsync(
            CognitiveFrame frame,
            LlmConfig config,
            CancellationToken cancellationToken)
        {
            Frame = frame;
            return Task.FromResult(new LlmCortexResponse(
                true,
                new CognitiveInsight("Устойчиво", "Наблюдаю", "Что помогало?", "low", 0.9),
                TimeSpan.Zero,
                null));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DeferredClient : ILlmCortexClient
    {
        private readonly TaskCompletionSource<LlmCortexResponse> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LlmCortexResponse> ObserveAsync(
            CognitiveFrame frame,
            LlmConfig config,
            CancellationToken cancellationToken) => _completion.Task.WaitAsync(cancellationToken);

        public void Complete(LlmCortexResponse response) => _completion.TrySetResult(response);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
