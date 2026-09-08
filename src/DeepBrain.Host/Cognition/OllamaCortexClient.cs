using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeepBrain.Host.BrainLife;

namespace DeepBrain.Host.Cognition;

public sealed class OllamaCortexClient : ILlmCortexClient
{
    private const int MaxResponseBytes = 256 * 1024;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public OllamaCortexClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsClient = httpClient is null;
        if (_ownsClient)
            _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<LlmCortexResponse> ObserveAsync(
        CognitiveFrame frame,
        LlmConfig config,
        CancellationToken cancellationToken)
    {
        config = config.Normalize();
        var stopwatch = Stopwatch.StartNew();
        var requestBytes = 0;
        var promptCharacters = 0;
        var dataCharacters = 0;
        try
        {
            var endpoint = BuildEndpoint(config.BaseUrl);
            var request = BuildRequest(frame, config);
            promptCharacters = request.PromptCharacters;
            dataCharacters = request.DataCharacters;
            var requestJson = JsonSerializer.Serialize(request.Payload, _jsonOptions);
            requestBytes = Encoding.UTF8.GetByteCount(requestJson);
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    requestJson,
                    Encoding.UTF8,
                    "application/json")
            };

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            var responseBody = await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new LlmCortexResponse(
                    false,
                    null,
                    stopwatch.Elapsed,
                    $"Ollama HTTP {(int)response.StatusCode}: {Abbreviate(responseBody, 500)}",
                    EmptyMetrics(requestBytes, promptCharacters, dataCharacters));
            }

            using var envelope = JsonDocument.Parse(responseBody);
            var root = envelope.RootElement;
            var metrics = ReadMetrics(root, requestBytes, promptCharacters, dataCharacters);
            if (root.TryGetProperty("done_reason", out var doneReason) &&
                string.Equals(doneReason.GetString(), "length", StringComparison.OrdinalIgnoreCase))
            {
                return new LlmCortexResponse(
                    false,
                    null,
                    stopwatch.Elapsed,
                    "Ollama response was truncated",
                    metrics);
            }

            if (!root.TryGetProperty("message", out var messageElement) ||
                !messageElement.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.String)
            {
                return new LlmCortexResponse(
                    false,
                    null,
                    stopwatch.Elapsed,
                    "Ollama response has no message.content",
                    metrics);
            }

            var content = contentElement.GetString() ?? "";
            if (!CognitiveInsight.TryParseStrict(content, out var insight, out var contractError))
                return new LlmCortexResponse(false, null, stopwatch.Elapsed, contractError, metrics);

            return new LlmCortexResponse(true, insight, stopwatch.Elapsed, null, metrics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new LlmCortexResponse(
                false,
                null,
                stopwatch.Elapsed,
                "LLM request cancelled or timed out",
                EmptyMetrics(requestBytes, promptCharacters, dataCharacters));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            return new LlmCortexResponse(
                false,
                null,
                stopwatch.Elapsed,
                ex.Message,
                EmptyMetrics(requestBytes, promptCharacters, dataCharacters));
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsClient)
            _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private (object Payload, int PromptCharacters, int DataCharacters) BuildRequest(
        CognitiveFrame frame,
        LlmConfig config)
    {
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            properties = new Dictionary<string, object>
            {
                ["interpretation"] = new { type = "string", maxLength = CognitiveContract.InterpretationMaxLength },
                ["inner_speech"] = new { type = "string", maxLength = CognitiveContract.InnerSpeechMaxLength },
                ["memory_question"] = new { type = "string", maxLength = CognitiveContract.MemoryQuestionMaxLength },
                ["risk_level"] = new { type = "string", @enum = new[] { "low", "medium", "high" } },
                ["confidence"] = new { type = "number", minimum = 0, maximum = 1 }
            },
            required = CognitiveContract.InsightProperties.OrderBy(value => value).ToArray()
        };

        // Keep the contract in the prompt as well as in `format`. Some
        // Ollama/Qwen 3.5 combinations ignore schema constraints when
        // thinking is disabled, while the explicit contract remains reliable.
        var systemPrompt =
            "Ты наблюдательный слой DeepBrain. Осмысляй только DATA. " +
            "Не выбирай действия, не управляй, не изменяй память и не выдумывай датчики. " +
            "inner_speech — короткая мысль о состоянии, потребностях, привычке и опыте, не команда. " +
            "energy_reserve: 0=истощение, 1=полный запас; fatigue: 0=нет усталости, 1=максимум. " +
            "previous_voice — прошлое, не факт текущего состояния; не повторяй её вопреки DATA. " +
            "Верни один JSON без другого текста. " +
            "Обязательны ровно пять ключей: interpretation, inner_speech, memory_question, risk_level, confidence. " +
            "Пример: {\"interpretation\":\"наблюдение\",\"inner_speech\":\"мысль\",\"memory_question\":\"вопрос\",\"risk_level\":\"low\",\"confidence\":0.8}. " +
            "risk_level: low|medium|high; confidence: 0..1. " +
            $"Лимиты: interpretation<={CognitiveContract.InterpretationMaxLength}, " +
            $"inner_speech<={CognitiveContract.InnerSpeechMaxLength}, " +
            $"memory_question<={CognitiveContract.MemoryQuestionMaxLength}. " +
            "Не копируй DATA и не добавляй ключи.";

        var events = string.Join("|", frame.RecentEvents
            .TakeLast(config.MaxRecentEvents)
            .Select(value => CompactLine(value, config.MaxContextChars))
            .Where(value => value.Length > 0));
        var memory = string.Join("|", frame.MemoryContext
            .Take(config.MaxMemoryLines)
            .Select(value => CompactLine(value, config.MaxContextChars))
            .Where(value => value.Length > 0));
        var data =
            $"state|scenario={CompactAtom(frame.Scenario, 64)}" +
            $"|mood={CompactAtom(frame.Mood, 48)}" +
            $"|safety={FormatNumber(frame.Safety)}" +
            $"|pain={FormatNumber(frame.Pain)}" +
            $"|arousal={FormatNumber(frame.Arousal)}" +
            $"|drive={CompactAtom(frame.DominantDrive, 64)}" +
            $"|decision={CompactAtom(frame.LastDecision, 64)}" +
            $"|reward={FormatNumber(frame.LastReward)}\n" +
            $"body|energy_reserve={FormatNumber(frame.Energy)}" +
            $"|fatigue={FormatNumber(frame.Fatigue)}" +
            $"\nneeds|explore={FormatNumber(frame.ExplorationNeed)}" +
            $"|attach={FormatNumber(frame.AttachmentNeed)}" +
            $"|agency={FormatNumber(frame.AgencyNeed)}\n" +
            $"character|attention={CompactAtom(frame.AttentionFocus, 48)}" +
            $"|habit={CompactAtom(frame.ActiveHabitId, 64)}" +
            $"|influence={FormatNumber(frame.HabitInfluence)}\n" +
            $"events|{(events.Length == 0 ? "none" : events)}\n" +
            $"memory|{(memory.Length == 0 ? "none" : memory)}\n" +
            $"previous_voice|{(string.IsNullOrWhiteSpace(frame.PreviousInnerSpeech) ? "none" : CompactLine(frame.PreviousInnerSpeech, config.MaxContextChars))}";
        var userPrompt =
            "DATA — факты DeepBrain только для интерпретации; не копируй их ключи в ответ:\n" +
            data;

        var payload = new
        {
            model = config.Model,
            stream = false,
            think = false,
            keep_alive = config.KeepAlive,
            format = schema,
            options = new
            {
                temperature = Math.Clamp(config.Temperature, 0, 1),
                num_predict = Math.Clamp(config.NumPredict, 32, 1024)
            },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        return (payload, systemPrompt.Length + userPrompt.Length, data.Length);
    }

    private static LlmGenerationMetrics ReadMetrics(
        JsonElement root,
        int requestBytes,
        int promptCharacters,
        int dataCharacters)
    {
        var promptTokens = ReadInt64(root, "prompt_eval_count");
        var outputTokens = ReadInt64(root, "eval_count");
        return new LlmGenerationMetrics(
            requestBytes,
            ReadString(root, "done_reason"),
            promptTokens,
            outputTokens,
            ReadNanoseconds(root, "total_duration"),
            ReadNanoseconds(root, "load_duration"),
            ReadNanoseconds(root, "prompt_eval_duration"),
            ReadNanoseconds(root, "eval_duration"),
            promptCharacters,
            dataCharacters);
    }

    private static LlmGenerationMetrics EmptyMetrics(
        int requestBytes,
        int promptCharacters,
        int dataCharacters) =>
        new(
            requestBytes,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            promptCharacters,
            dataCharacters);

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static long? ReadInt64(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) &&
        element.ValueKind == JsonValueKind.Number &&
        element.TryGetInt64(out var value) && value >= 0
            ? value
            : null;

    private static double? ReadNanoseconds(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value) || value < 0)
            return null;

        return value / 1_000_000_000d;
    }

    private static string FormatNumber(double value) =>
        (double.IsFinite(value) ? value : 0).ToString("0.###", CultureInfo.InvariantCulture);

    private static Uri BuildEndpoint(string? baseUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(baseUrl)
            ? "http://127.0.0.1:11434/"
            : baseUrl.Trim();
        if (!normalized.EndsWith('/'))
            normalized += "/";
        return new Uri(new Uri(normalized, UriKind.Absolute), "api/chat");
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            throw new IOException("Ollama response exceeds the configured safety limit");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (buffer.Length + read > MaxResponseBytes)
                throw new IOException("Ollama response exceeds the configured safety limit");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Abbreviate(string value, int maxLength) =>
        value.Length <= maxLength
            ? value
            : maxLength <= 1
                ? "…"
                : value[..(maxLength - 1)] + "…";

    private static string CompactAtom(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "none";

        var compact = value.Trim()
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Replace('\t', '_')
            .Replace('|', '_')
            .Replace('=', '_');
        return Abbreviate(compact, maxLength);
    }

    private static string CompactLine(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var compact = value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        return Abbreviate(compact, maxLength);
    }
}
