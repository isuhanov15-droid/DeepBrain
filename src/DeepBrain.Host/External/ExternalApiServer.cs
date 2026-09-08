using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Host.Cognition;

namespace DeepBrain.Host.External;

public sealed record ExternalApiStatus(
    bool Enabled,
    bool Listening,
    string Prefix,
    bool TokenRequired,
    string? LastError
);

public sealed class ExternalApiServer : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Func<ExternalApiConfig> _configProvider;
    private readonly ExternalEventHub _events;
    private readonly LlmCortexOrchestrator _cortex;
    private readonly Func<string> _mlStatusProvider;
    private readonly Func<string> _memoryStatusProvider;
    private readonly Func<string> _habitsStatusProvider;
    private readonly Action<string> _log;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private HttpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptLoop;
    private ExternalApiConfig _activeConfig = ExternalApiConfig.Default;
    private bool _listening;
    private string? _lastError;

    public ExternalApiServer(
        Func<ExternalApiConfig> configProvider,
        ExternalEventHub events,
        LlmCortexOrchestrator cortex,
        Func<string> mlStatusProvider,
        Func<string> memoryStatusProvider,
        Func<string> habitsStatusProvider,
        Action<string> log)
    {
        _configProvider = configProvider;
        _events = events;
        _cortex = cortex;
        _mlStatusProvider = mlStatusProvider;
        _memoryStatusProvider = memoryStatusProvider;
        _habitsStatusProvider = habitsStatusProvider;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_listener is not null || _lifetime is not null)
                return Task.CompletedTask;

            _activeConfig = (_configProvider() ?? ExternalApiConfig.Default).Normalize();
            if (!_activeConfig.Enable)
            {
                _log("Внешний API отключён конфигурацией");
                return Task.CompletedTask;
            }

            var token = ReadToken(_activeConfig);
            if (!ValidateConfiguration(_activeConfig, token, out var error))
            {
                _lastError = error;
                _log($"Внешний API не запущен: {error}");
                return Task.CompletedTask;
            }

            try
            {
                _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _listener = new HttpListener();
                _listener.Prefixes.Add(_activeConfig.Prefix);
                _listener.Start();
                _listening = true;
                _lastError = null;
                _acceptLoop = AcceptLoopAsync(_lifetime.Token);
                _log($"Внешний API слушает {_activeConfig.Prefix}; token={RussianBool(TokenRequired(_activeConfig))}");
            }
            catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
            {
                _lastError = ex.Message;
                _listening = false;
                _listener?.Close();
                _listener = null;
                _lifetime?.Dispose();
                _lifetime = null;
                _log($"Внешний API не запущен: {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    public ExternalApiStatus GetStatus()
    {
        lock (_sync)
        {
            var config = _activeConfig.Enable
                ? _activeConfig
                : (_configProvider() ?? ExternalApiConfig.Default).Normalize();
            return new ExternalApiStatus(
                config.Enable,
                _listening,
                config.Prefix,
                TokenRequired(config),
                _lastError);
        }
    }

    public string GetStatusLine()
    {
        var status = GetStatus();
        return $"включён={RussianBool(status.Enabled)} слушает={RussianBool(status.Listening)} " +
               $"адрес={status.Prefix} token={RussianBool(status.TokenRequired)} " +
               $"последняя ошибка={status.LastError ?? "нет"}";
    }

    public static bool ValidateConfiguration(
        ExternalApiConfig config,
        string? token,
        out string? error)
    {
        error = null;
        var normalized = config.Normalize();
        if (!normalized.Enable)
            return true;

        if (!TryGetPrefixHost(normalized.Prefix, out var host))
        {
            error = "некорректный HTTP prefix";
            return false;
        }

        if (TokenRequired(normalized) && string.IsNullOrWhiteSpace(token))
        {
            error = $"для адреса {host} требуется bearer token в переменной {normalized.TokenEnvironmentVariable}";
            return false;
        }

        if (!normalized.Prefix.EndsWith("/", StringComparison.Ordinal))
        {
            error = "HTTP prefix должен завершаться символом /";
            return false;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        CancellationTokenSource? lifetime;
        HttpListener? listener;
        lock (_sync)
        {
            loop = _acceptLoop;
            lifetime = _lifetime;
            listener = _listener;
            _acceptLoop = null;
            _lifetime = null;
            _listener = null;
            _listening = false;
        }

        if (lifetime is not null)
            lifetime.Cancel();
        listener?.Close();

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lifetime?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                var listener = _listener;
                if (listener is null)
                    return;
                context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    lock (_sync)
                        _lastError = ex.Message;
                }
                return;
            }

            _ = HandleSafelyAsync(context, cancellationToken);
        }
    }

    private async Task HandleSafelyAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await HandleAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryClose(context.Response);
        }
        catch (Exception ex)
        {
            lock (_sync)
                _lastError = ex.Message;
            try
            {
                await WriteJsonAsync(context.Response, HttpStatusCode.InternalServerError,
                    new { error = "internal_error" }, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                TryClose(context.Response);
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        ApplyCors(context);
        if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            context.Response.Close();
            return;
        }

        var path = NormalizePath(context.Request.Url?.AbsolutePath);
        if (path == "/api/v1/health" && IsMethod(context, "GET"))
        {
            await WriteJsonAsync(context.Response, HttpStatusCode.OK, new
            {
                status = "ok",
                contractVersion = ExternalContract.Version,
                utc = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!IsAuthorized(context.Request))
        {
            context.Response.Headers["WWW-Authenticate"] = "Bearer";
            await WriteJsonAsync(context.Response, HttpStatusCode.Unauthorized,
                new { error = "unauthorized" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path == "/api/v1/capabilities" && IsMethod(context, "GET"))
        {
            await WriteJsonAsync(context.Response, HttpStatusCode.OK, new
            {
                contractVersion = ExternalContract.Version,
                cortexContractVersion = CognitiveContract.Version,
                access = "observation",
                endpoints = new[]
                {
                    "GET /api/v1/state",
                    "GET /api/v1/outputs/latest",
                    "GET /api/v1/system/status",
                    "GET /api/v1/llm/status",
                    "GET /api/v1/llm/last",
                    "GET /api/v1/llm/journal",
                    "POST /api/v1/llm/observe",
                    "GET /api/v1/events"
                },
                transports = new[] { "http", "sse" },
                plannedAdapters = new[] { "mqtt", "home-assistant", "ros2" },
                directActuation = false
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path == "/api/v1/state" && IsMethod(context, "GET"))
        {
            var state = _events.GetLatestState();
            await WriteOptionalAsync(context.Response, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path == "/api/v1/outputs/latest" && IsMethod(context, "GET"))
        {
            var output = _events.GetLatestOutput();
            await WriteOptionalAsync(context.Response, output, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path == "/api/v1/system/status" && IsMethod(context, "GET"))
        {
            await WriteJsonAsync(context.Response, HttpStatusCode.OK, new
            {
                ml = _mlStatusProvider(),
                memory = _memoryStatusProvider(),
                habits = _habitsStatusProvider(),
                llm = _cortex.GetStatus(),
                api = GetStatus()
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path == "/api/v1/llm/status" && IsMethod(context, "GET"))
        {
            await WriteJsonAsync(context.Response, HttpStatusCode.OK, _cortex.GetStatus(), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (path == "/api/v1/llm/last" && IsMethod(context, "GET"))
        {
            await WriteOptionalAsync(context.Response, _cortex.GetLastInsight(), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path == "/api/v1/llm/journal" && IsMethod(context, "GET"))
        {
            var count = int.TryParse(context.Request.QueryString["count"], out var parsed)
                ? Math.Clamp(parsed, 1, 100)
                : 20;
            await WriteJsonAsync(context.Response, HttpStatusCode.OK, _cortex.GetRecentJournal(count), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (path == "/api/v1/llm/observe" && IsMethod(context, "POST"))
        {
            var queued = _cortex.TryQueueObservation("external-api", out var frameId);
            await WriteJsonAsync(
                context.Response,
                queued ? HttpStatusCode.Accepted : HttpStatusCode.Conflict,
                new { queued, frameId, status = _cortex.GetStatus() },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path == "/api/v1/events" && IsMethod(context, "GET"))
        {
            await StreamEventsAsync(context.Response, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(context.Response, HttpStatusCode.NotFound,
            new { error = "not_found" }, cancellationToken).ConfigureAwait(false);
    }

    private async Task StreamEventsAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.SendChunked = true;
        response.KeepAlive = true;
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = _events.Subscribe(_activeConfig.SubscriberBufferCapacity);
        await WriteSseCommentAsync(response, "deepbrain connected", cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var envelope in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(envelope, _jsonOptions);
                var data = $"id: {envelope.Sequence}\nevent: {envelope.Type}\ndata: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(data);
                await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await response.OutputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or OperationCanceledException)
        {
        }
        finally
        {
            TryClose(response);
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (!TokenRequired(_activeConfig))
            return true;

        var expected = ReadToken(_activeConfig);
        var authorization = request.Headers["Authorization"];
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(expected) ||
            string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var supplied = authorization[prefix.Length..].Trim();
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private void ApplyCors(HttpListenerContext context)
    {
        var origin = context.Request.Headers["Origin"];
        if (string.IsNullOrWhiteSpace(origin))
            return;

        var allowed = _activeConfig.AllowedOrigins.Any(candidate =>
            candidate == "*" || string.Equals(candidate, origin, StringComparison.OrdinalIgnoreCase));
        if (!allowed)
            return;

        context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        context.Response.Headers["Vary"] = "Origin";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
    }

    private async Task WriteOptionalAsync<T>(
        HttpListenerResponse response,
        T? value,
        CancellationToken cancellationToken) where T : class
    {
        if (value is null)
        {
            await WriteJsonAsync(response, HttpStatusCode.NotFound,
                new { error = "not_available" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(response, HttpStatusCode.OK, value, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteJsonAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        object value,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.Close();
    }

    private static async Task WriteSseCommentAsync(
        HttpListenerResponse response,
        string comment,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes($": {comment}\n\n");
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await response.OutputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsMethod(HttpListenerContext context, string method) =>
        string.Equals(context.Request.HttpMethod, method, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (normalized.Length > 1)
            normalized = normalized.TrimEnd('/');
        return normalized;
    }

    private static string? ReadToken(ExternalApiConfig config) =>
        Environment.GetEnvironmentVariable(config.TokenEnvironmentVariable)?.Trim();

    private static bool TokenRequired(ExternalApiConfig config) =>
        config.RequireToken || !IsLoopbackPrefix(config.Prefix);

    private static bool IsLoopbackPrefix(string prefix) =>
        TryGetPrefixHost(prefix, out var host) &&
        (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase));

    private static bool TryGetPrefixHost(string prefix, out string host)
    {
        host = "";
        if (string.IsNullOrWhiteSpace(prefix) ||
            !prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return false;

        var authorityStart = prefix.IndexOf("//", StringComparison.Ordinal) + 2;
        var slash = prefix.IndexOf('/', authorityStart);
        var authority = slash >= 0 ? prefix[authorityStart..slash] : prefix[authorityStart..];
        if (authority.Length == 0)
            return false;

        if (authority[0] == '[')
        {
            var bracket = authority.IndexOf(']');
            if (bracket < 0)
                return false;
            host = authority[..(bracket + 1)];
            return true;
        }

        var colon = authority.LastIndexOf(':');
        host = colon > 0 ? authority[..colon] : authority;
        return host.Length > 0;
    }

    private static void TryClose(HttpListenerResponse response)
    {
        try
        {
            response.Close();
        }
        catch
        {
        }
    }

    private static string RussianBool(bool value) => value ? "да" : "нет";
}
