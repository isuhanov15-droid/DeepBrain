using System.Globalization;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.Cognition;

public sealed class LlmCortexOrchestrator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Func<LlmConfig> _configProvider;
    private readonly Func<IReadOnlyList<string>> _memoryProvider;
    private readonly Action<CognitiveInsightEnvelope> _onInsight;
    private readonly Action<string> _log;
    private readonly ILlmCortexClient _client;
    private readonly CognitiveJournal _journal;

    private CancellationTokenSource? _lifetime;
    private Task? _activeRequest;
    private LifeStateDto? _latestState;
    private CognitiveInsightEnvelope? _lastInsight;
    private long _latestStateTick;
    private long _lastQueuedFrameId;
    private long _completed;
    private long _failed;
    private long _droppedAsStale;
    private long _droppedLowConfidence;
    private long _lastAutoQueuedTick;
    private long _activeFrameTick;
    private long _lastFrameAgeTicks;
    private int _inFlight;
    private double? _lastLatencySeconds;
    private LlmGenerationMetrics? _lastMetrics;
    private DateTimeOffset? _activeRequestStartedAt;
    private DateTimeOffset? _lastSuccessAt;
    private string? _lastError;
    private string? _lastPublishedScenario;
    private string? _lastPublishedMood;
    private string? _lastPublishedDrive;

    public LlmCortexOrchestrator(
        Func<LlmConfig> configProvider,
        Func<IReadOnlyList<string>> memoryProvider,
        Action<CognitiveInsightEnvelope> onInsight,
        Action<string> log,
        ILlmCortexClient? client = null)
    {
        _configProvider = configProvider;
        _memoryProvider = memoryProvider;
        _onInsight = onInsight;
        _log = log;
        _client = client ?? new OllamaCortexClient();
        _journal = new CognitiveJournal(log);
    }

    public void Start(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_lifetime is not null)
                return;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        var config = GetConfig();
        _journal.Configure(config);
        lock (_sync)
            _lastInsight ??= _journal.GetLast();
        _log(config.Enable
            ? $"LLM Cortex включён: provider={config.Provider} model={config.Model} auto={RussianBool(config.AutoObserve)}"
            : "LLM Cortex отключён конфигурацией");
    }

    public void PublishState(LifeStateDto state)
    {
        string? transition;
        lock (_sync)
        {
            _latestState = state;
            _latestStateTick = state.Tick;
            transition = ReadTransitionLocked(state);
        }

        var config = GetConfig();
        if (!config.Enable || !config.AutoObserve || state.Tick <= 0)
            return;

        if (Volatile.Read(ref _inFlight) == 1)
            return;

        long lastAutoTick;
        lock (_sync)
            lastAutoTick = _lastAutoQueuedTick;
        var age = Math.Max(0, state.Tick - lastAutoTick);
        var firstObservation = lastAutoTick == 0;
        var intervalDue = firstObservation || age >= config.ObserveEveryTicks;
        var transitionDue = config.ObserveOnTransitions && transition is not null &&
                            (firstObservation || age >= config.MinObserveGapTicks);
        if (!intervalDue && !transitionDue)
            return;

        var reason = firstObservation
            ? "auto.start"
            : transitionDue
                ? $"auto.transition:{transition}"
                : "auto.interval";
        if (TryQueueObservation(reason, out _))
        {
            lock (_sync)
                _lastAutoQueuedTick = state.Tick;
        }
    }

    public bool TryQueueObservation(string reason, out long frameId)
    {
        frameId = 0;
        var config = GetConfig();
        if (!config.Enable)
        {
            SetError("LLM Cortex отключён конфигурацией");
            return false;
        }

        if (!string.Equals(config.Provider, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            SetError($"Неподдерживаемый LLM provider: {config.Provider}");
            return false;
        }

        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            SetError("LLM запрос уже выполняется");
            return false;
        }

        LifeStateDto? state;
        CancellationToken cancellationToken;
        lock (_sync)
        {
            state = _latestState;
            cancellationToken = _lifetime?.Token ?? CancellationToken.None;
        }

        if (state is null)
        {
            Interlocked.Exchange(ref _inFlight, 0);
            SetError("Снимок состояния ещё не получен");
            return false;
        }

        frameId = Interlocked.Increment(ref _lastQueuedFrameId);
        var frame = BuildFrame(state, frameId, config);
        lock (_sync)
        {
            _activeFrameTick = frame.Tick;
            _activeRequestStartedAt = DateTimeOffset.UtcNow;
            _lastError = null;
        }
        var request = ObserveAsync(
            frame,
            string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim(),
            config,
            cancellationToken);
        lock (_sync)
            _activeRequest = request;
        _log($"LLM Cortex: запрос frame={frame.FrameId} тик={frame.Tick} причина={reason}");
        return true;
    }

    public LlmCortexStatus GetStatus()
    {
        var config = GetConfig();
        lock (_sync)
        {
            var inFlight = Volatile.Read(ref _inFlight) == 1;
            var inFlightFrameTick = inFlight ? _activeFrameTick : 0;
            var inFlightAgeTicks = inFlight
                ? Math.Max(0, _latestStateTick - inFlightFrameTick)
                : 0;
            var inFlightStartedAt = inFlight ? _activeRequestStartedAt : null;
            var inFlightSeconds = inFlightStartedAt is { } startedAt
                ? Math.Max(0, (DateTimeOffset.UtcNow - startedAt).TotalSeconds)
                : 0;
            return new LlmCortexStatus(
                config.Enable,
                config.Provider,
                config.Model,
                inFlight,
                _latestStateTick,
                _lastQueuedFrameId,
                _completed,
                _failed,
                _droppedAsStale,
                _lastSuccessAt,
                _lastError,
                inFlightFrameTick,
                inFlightAgeTicks,
                _lastLatencySeconds,
                _lastFrameAgeTicks,
                _lastMetrics,
                inFlightStartedAt,
                inFlightSeconds,
                _droppedLowConfidence,
                _lastAutoQueuedTick,
                _journal.GetStatus());
        }
    }

    public CognitiveInsightEnvelope? GetLastInsight()
    {
        lock (_sync)
            return _lastInsight;
    }

    public IReadOnlyList<CognitiveInsightEnvelope> GetRecentJournal(int count) =>
        _journal.GetRecent(count);

    public string GetStatusLine()
    {
        var status = GetStatus();
        var current = status.InFlight
            ? $" кадр={status.LastQueuedFrameId}@{status.InFlightFrameTick} " +
              $"в работе={status.InFlightSeconds:F1}с возраст={status.InFlightAgeTicks}т"
            : "";
        var last = status.LastLatencySeconds is null
            ? " последний=нет"
            : $" последний={status.LastLatencySeconds:F1}с возраст={status.LastFrameAgeTicks}т" +
              FormatMetrics(status.LastMetrics);
        return $"включён={RussianBool(status.Enabled)} provider={status.Provider} model={status.Model} " +
               $"запрос={RussianBool(status.InFlight)} последний тик={status.LatestStateTick} " +
               $"готово={status.Completed} ошибок={status.Failed} устарело={status.DroppedAsStale} " +
               $"низкая уверенность={status.DroppedLowConfidence} авто-тик={status.LastAutoQueuedTick} " +
               $"последняя ошибка={status.LastError ?? "нет"}{current}{last}";
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? lifetime;
        Task? activeRequest;
        lock (_sync)
        {
            lifetime = _lifetime;
            _lifetime = null;
            activeRequest = _activeRequest;
        }

        if (lifetime is not null)
            lifetime.Cancel();

        if (activeRequest is not null)
        {
            try
            {
                await activeRequest.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lifetime?.Dispose();
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ObserveAsync(
        CognitiveFrame frame,
        string reason,
        LlmConfig config,
        CancellationToken hostCancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
            var response = await _client.ObserveAsync(frame, config, timeout.Token).ConfigureAwait(false);
            long resultAtTick;
            long frameAgeTicks;
            lock (_sync)
            {
                resultAtTick = _latestStateTick;
                frameAgeTicks = Math.Max(0, resultAtTick - frame.Tick);
                _lastLatencySeconds = response.Latency.TotalSeconds;
                _lastFrameAgeTicks = frameAgeTicks;
                _lastMetrics = response.Metrics;
            }
            if (!response.Success || response.Insight is null)
            {
                lock (_sync)
                {
                    _failed++;
                    _lastError = response.Error ?? "LLM вернула пустой ответ";
                }
                _log($"LLM Cortex: запрос frame={frame.FrameId} отклонён: " +
                     $"{response.Error ?? "пустой ответ"}; " +
                     $"время={response.Latency.TotalSeconds:F1}с возраст={frameAgeTicks}т" +
                     FormatMetrics(response.Metrics));
                return;
            }

            if (frameAgeTicks > config.MaxStalenessTicks)
            {
                lock (_sync)
                {
                    _droppedAsStale++;
                    _lastError = $"LLM ответ устарел на {frameAgeTicks} тиков";
                }
                _log($"LLM Cortex: устаревший ответ frame={frame.FrameId} отброшен; " +
                     $"время={response.Latency.TotalSeconds:F1}с возраст={frameAgeTicks}т" +
                     FormatMetrics(response.Metrics));
                return;
            }

            if (response.Insight.Confidence < config.MinConfidence)
            {
                lock (_sync)
                {
                    _droppedLowConfidence++;
                    _lastError = $"LLM уверенность {response.Insight.Confidence:F2} ниже порога {config.MinConfidence:F2}";
                }
                _log($"LLM Cortex: ответ frame={frame.FrameId} не принят из-за низкой уверенности " +
                     $"{response.Insight.Confidence:F2}<{config.MinConfidence:F2}");
                return;
            }

            var envelope = new CognitiveInsightEnvelope(
                CognitiveContract.Version,
                frame.FrameId,
                frame.Tick,
                resultAtTick,
                DateTimeOffset.UtcNow,
                config.Provider,
                config.Model,
                reason,
                response.Latency,
                response.Insight,
                response.Metrics);

            lock (_sync)
            {
                _lastInsight = envelope;
                _completed++;
                _lastSuccessAt = envelope.CreatedAt;
                _lastError = null;
            }

            _journal.Configure(config);
            _journal.Append(envelope);
            try
            {
                _onInsight(envelope);
            }
            catch (Exception ex)
            {
                _log($"LLM Cortex: наблюдение принято, но публикация внутреннего голоса не удалась: {ex.Message}");
            }
            _log($"LLM НАБЛЮДЕНИЕ: frame={frame.FrameId} тик={frame.Tick} " +
                 $"риск={response.Insight.RiskLevel} уверенность={response.Insight.Confidence:F2} " +
                 $"время={response.Latency.TotalSeconds:F1}с возраст={frameAgeTicks}т" +
                 FormatMetrics(response.Metrics));
            if (!string.IsNullOrWhiteSpace(response.Insight.InnerSpeech))
                _log($"ВНУТРЕННИЙ ГОЛОС LLM: {response.Insight.InnerSpeech}");
            if (!string.IsNullOrWhiteSpace(response.Insight.MemoryQuestion))
                _log($"ВОПРОС К ПАМЯТИ LLM: {response.Insight.MemoryQuestion}");
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _failed++;
                _lastError = ex.Message;
            }
            _log($"LLM Cortex: необработанная ошибка запроса: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    private CognitiveFrame BuildFrame(LifeStateDto state, long frameId, LlmConfig config)
    {
        var memory = SafeMemoryContext(config.MaxMemoryLines, config.MaxContextChars);
        string previousInnerSpeech;
        lock (_sync)
            previousInnerSpeech = _lastInsight?.Insight.InnerSpeech ?? "";
        return new CognitiveFrame(
            CognitiveContract.Version,
            frameId,
            state.Tick,
            state.Ts,
            state.Scenario?.Name ?? "unknown",
            state.Affect.Mood ?? "unknown",
            FiniteOrZero(state.Homeostasis.Safety),
            FiniteOrZero(state.Homeostasis.Pain),
            FiniteOrZero(state.Affect.Arousal),
            state.DominantDrive ?? "",
            state.LastDecision ?? "",
            FiniteOrZero(state.LastReward),
            ReadRecentEvents(state.RecentEvents, config.MaxRecentEvents, config.MaxContextChars),
            memory)
        {
            Energy = FiniteOrZero(state.Homeostasis.Energy),
            Fatigue = FiniteOrZero(state.Homeostasis.Fatigue),
            ExplorationNeed = FiniteOrZero(state.Instincts.Exploration),
            AttachmentNeed = FiniteOrZero(state.Instincts.Attachment),
            AgencyNeed = FiniteOrZero(state.Instincts.Agency),
            AttentionFocus = state.Attention?.Focus1 ?? "",
            ActiveHabitId = state.Character?.ActiveHabitId ?? "",
            HabitInfluence = FiniteOrZero(state.Character?.HabitInfluence ?? 0),
            PreviousInnerSpeech = Abbreviate(previousInnerSpeech, config.MaxContextChars)
        };
    }

    private IReadOnlyList<string> SafeMemoryContext(int maxLines, int maxChars)
    {
        try
        {
            return (_memoryProvider() ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(maxLines)
                .Select(value => Abbreviate(value, maxChars))
                .ToArray();
        }
        catch (Exception ex)
        {
            _log($"LLM Cortex: не удалось получить контекст памяти: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ReadRecentEvents(
        IReadOnlyList<WorldEventDto>? events,
        int maxEvents,
        int maxChars)
    {
        if (events is null || events.Count == 0 || maxEvents <= 0)
            return Array.Empty<string>();

        return events
            .TakeLast(maxEvents)
            .Select(value =>
                $"{CompactIdentifier(value.Type, 40)}:" +
                $"{FormatNumber(value.Severity)}:" +
                $"{FormatNumber(value.Salience)}:" +
                $"{(value.Consumed ? 1 : 0)}")
            .Select(value => Abbreviate(value, maxChars))
            .ToArray();
    }

    private string? ReadTransitionLocked(LifeStateDto state)
    {
        var scenario = state.Scenario?.Name ?? "unknown";
        var mood = state.Affect.Mood ?? "unknown";
        var drive = state.DominantDrive ?? "";

        string? transition = null;
        if (_lastPublishedScenario is null)
            transition = "start";
        else if (!string.Equals(scenario, _lastPublishedScenario, StringComparison.Ordinal))
            transition = "scenario";
        else if (!string.Equals(mood, _lastPublishedMood, StringComparison.Ordinal))
            transition = "mood";
        else if (!string.Equals(drive, _lastPublishedDrive, StringComparison.Ordinal))
            transition = "drive";

        _lastPublishedScenario = scenario;
        _lastPublishedMood = mood;
        _lastPublishedDrive = drive;
        return transition;
    }

    private static string FormatMetrics(LlmGenerationMetrics? metrics)
    {
        if (metrics is null)
            return "";

        var promptTokens = metrics.PromptTokens?.ToString() ?? "?";
        var outputTokens = metrics.OutputTokens?.ToString() ?? "?";
        var total = metrics.TotalSeconds is { } totalSeconds
            ? $" ollama={totalSeconds:F1}с"
            : "";
        return $" json={metrics.RequestBytes}Б data={metrics.DataCharacters}зн " +
               $"promptChars={metrics.PromptCharacters} prompt={promptTokens} output={outputTokens}{total}";
    }

    private static string Abbreviate(string value, int maxLength) =>
        value.Length <= maxLength
            ? value
            : maxLength <= 1
                ? "…"
                : value[..(maxLength - 1)] + "…";

    private static string CompactIdentifier(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var compact = value.Trim()
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Replace('\t', '_')
            .Replace(':', '_')
            .Replace('|', '_');
        return Abbreviate(compact, maxLength);
    }

    private static string FormatNumber(double value) =>
        FiniteOrZero(value).ToString("0.###", CultureInfo.InvariantCulture);

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;

    private LlmConfig GetConfig() => (_configProvider() ?? LlmConfig.Default).Normalize();

    private void SetError(string error)
    {
        lock (_sync)
            _lastError = error;
    }

    private static string RussianBool(bool value) => value ? "да" : "нет";
}
