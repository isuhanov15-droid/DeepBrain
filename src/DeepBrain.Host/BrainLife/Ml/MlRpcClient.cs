using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DeepBrain.Host.BrainLife.Ml;

internal sealed class MlRpcClient : IAsyncDisposable
{
    private const int MaxFrameBytes = 4 * 1024 * 1024;
    private readonly Action<string> _log;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private MlRemoteConfig _config;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private DateTime _nextReconnectAt = DateTime.MinValue;
    private string? _lastError;
    private double _lastRttMs;
    private bool _connected;

    public MlRpcClient(MlRemoteConfig config, Action<string> log)
    {
        _config = config;
        _log = log;
    }

    public bool IsConnected => _connected;
    public string? LastError => _lastError;
    public double LastRttMs => _lastRttMs;

    public void UpdateConfig(MlRemoteConfig config)
    {
        if (!string.Equals(_config.Host, config.Host, StringComparison.OrdinalIgnoreCase) || _config.Port != config.Port)
            Disconnect();
        _config = config;
    }

    public bool TryConnect()
    {
        try
        {
            return EnsureConnectedAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    public async Task<TResponse?> CallAsync<TRequest, TResponse>(string method, TRequest payload, CancellationToken ct)
    {
        if (!await EnsureConnectedAsync(ct))
            return default;

        await _ioLock.WaitAsync(ct);
        try
        {
            var req = new RpcRequest(1, Guid.NewGuid().ToString("N"), method, payload);
            var json = JsonSerializer.Serialize(req, _json);
            await WriteFrameAsync(json, ct);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var responseJson = await ReadFrameAsync(ct);
            sw.Stop();
            _lastRttMs = sw.Elapsed.TotalMilliseconds;
            if (responseJson == null)
            {
                _lastError = "RPC вернул пустой ответ";
                Disconnect();
                return default;
            }

            RpcResponse? resp;
            try
            {
                resp = JsonSerializer.Deserialize<RpcResponse>(responseJson, _json);
            }
            catch (JsonException jex)
            {
                LogJsonException("rpc.response", responseJson, jex);
                _lastError = jex.Message;
                Disconnect();
                return default;
            }
            if (resp == null)
            {
                _lastError = "Не удалось разобрать ответ RPC";
                Disconnect();
                return default;
            }

            if (!resp.ok)
            {
                _lastError = resp.error?.message ?? "Ошибка RPC";
                return default;
            }

            if (resp.result is null)
                return default;

            var text = resp.result.Value.GetRawText();
            try
            {
                return JsonSerializer.Deserialize<TResponse>(text, _json);
            }
            catch (JsonException jex)
            {
                LogJsonException("rpc.result", text, jex);
                _lastError = jex.Message;
                return default;
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Disconnect();
            return default;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void Disconnect()
    {
        _connected = false;
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        _ioLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_connected && _client?.Connected == true && _stream != null)
            return true;

        if (DateTime.UtcNow < _nextReconnectAt)
            return false;

        _nextReconnectAt = DateTime.UtcNow.AddMilliseconds(Math.Max(200, _config.ReconnectMs));

        try
        {
            var client = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(_config.TimeoutMs > 0 ? _config.TimeoutMs : 2000);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await client.ConnectAsync(_config.Host, _config.Port, linked.Token);
            client.NoDelay = true;
            _client = client;
            _stream = client.GetStream();
            _connected = true;
            _lastError = null;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _connected = false;
            return false;
        }
    }

    private async Task WriteFrameAsync(string json, CancellationToken ct)
    {
        if (_stream == null) throw new InvalidOperationException("Поток не подключён");
        var payload = Encoding.UTF8.GetBytes(json);
        var len = payload.Length;
        if (len <= 0 || len > MaxFrameBytes)
            throw new InvalidDataException($"Недопустимая длина данных: {len}");
        var lenBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBytes.AsSpan(), len);
        await _stream.WriteAsync(lenBytes, ct);
        await _stream.WriteAsync(payload, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task<string?> ReadFrameAsync(CancellationToken ct)
    {
        if (_stream == null) throw new InvalidOperationException("Поток не подключён");
        var lenBuf = new byte[4];
        if (!await ReadExactAsync(lenBuf, ct))
            return null;
        var len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf.AsSpan());
        if (len <= 0 || len > MaxFrameBytes)
            throw new InvalidDataException($"Недопустимая длина кадра: {len}");
        var payload = new byte[len];
        if (!await ReadExactAsync(payload, ct))
            return null;
        return Encoding.UTF8.GetString(payload);
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        if (_stream == null) return false;
        int readTotal = 0;
        while (readTotal < buffer.Length)
        {
            int n = await _stream.ReadAsync(buffer.AsMemory(readTotal, buffer.Length - readTotal), ct);
            if (n == 0) return false;
            readTotal += n;
        }
        return true;
    }

    private sealed record RpcRequest(int v, string id, string method, object? @params);
    private sealed record RpcResponse(int v, string id, bool ok, JsonElement? result, RpcError? error);
    private sealed record RpcError(string code, string message, string? details);

    private void LogJsonException(string stage, string json, JsonException ex)
    {
        var max = Math.Min(json.Length, 1000);
        var snippet = json[..max];
        _log($"Предупреждение: ошибка разбора JSON ({stage}) путь={ex.Path} сообщение={ex.Message} данные={snippet}");
    }
}
