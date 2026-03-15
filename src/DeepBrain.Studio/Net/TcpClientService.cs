using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using DeepBrain.Shared.Net;
using DeepBrain.Shared.Input;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Trace;
using System.Text.Json;

namespace DeepBrain.Studio.Net;

public sealed class TcpClientService : IAsyncDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _pongTcs;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => _client?.Connected == true;

    public event Action<string>? OnLog;
    public event Action<string>? OnInfo;
    public event Action<long, long, string, string>? OnState;
    public event Action<LifeStateDto>? OnLifeState;
    public event Action<LifeOutputDto>? OnLifeOutput;
    public event Action<string>? OnTrace;


    public async Task ConnectAsync(string host, int port)
    {
        _cts = new CancellationTokenSource();
        _client = new TcpClient();
        try
        {
            await _client.ConnectAsync(host, port);
            _stream = _client.GetStream();

            OnInfo?.Invoke($"Connected to {host}:{port}");

            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            OnInfo?.Invoke($"Connect error: {ex.Message}");
            await DisconnectAsync();
        }
    }

    public async Task SubscribeLogsAsync()
    {
        if (_stream is null) return;

        var env = new Envelope(Msg.LogsSubscribe, Guid.NewGuid().ToString("N"), NowMs(), new { });
        await SendAsync(env, _cts?.Token ?? CancellationToken.None);
    }

    public async Task PingAsync()
    {
        if (_stream is null) return;

        var env = new Envelope(Msg.Ping, Guid.NewGuid().ToString("N"), NowMs(), new { });
        await SendAsync(env, _cts?.Token ?? CancellationToken.None);
    }

    public async Task<bool> PingWithTimeoutAsync(int timeoutMs = 1000)
    {
        if (_stream is null) return false;

        _pongTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await PingAsync();

        using var cts = new CancellationTokenSource(timeoutMs);
        await using var reg = cts.Token.Register(() => _pongTcs.TrySetResult(false));
        return await _pongTcs.Task;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var frame = await Framing.ReadFrameAsync(_stream, Framing.MaxFrameBytes, ct);
                if (frame is null)
                {
                    OnInfo?.Invoke("ReadLoop: disconnected");
                    break;
                }

                Envelope env;
                try
                {
                    env = JsonWire.Deserialize(frame);
                }
                catch (JsonException jex)
                {
                    var head = HexHead(frame, 16);
                    OnInfo?.Invoke($"Invalid frame payload (first bytes {head}): {jex.Message}");
                    break;
                }

                HandleIncoming(env);
            }
        }
        catch (OperationCanceledException) { }
        catch (InvalidDataException ex)
        {
            OnInfo?.Invoke($"ReadLoop invalid frame: {ex.Message}");
        }
        catch (Exception ex)
        {
            OnInfo?.Invoke($"ReadLoop error: {ex}");
        }
        finally
        {
            OnInfo?.Invoke("Disconnected.");
            if (_client is not null)
                await DisconnectAsync();
        }
    }



    private void HandleIncoming(Envelope env)
    {
        try
        {
            switch (env.Type)
            {
                case Msg.LogAppend:
                    if (env.Payload is JsonElement je && je.TryGetProperty("text", out var t))
                        OnLog?.Invoke(t.GetString() ?? "");
                    else
                        OnLog?.Invoke(env.Payload?.ToString() ?? "(log)");
                    break;

            case Msg.Pong:
                OnInfo?.Invoke("pong ✅");
                _pongTcs?.TrySetResult(true);
                break;

            case Msg.BrainState:
                if (env.Payload is JsonElement s)
                {
                    long tick = s.TryGetProperty("tick", out var tickEl) ? tickEl.GetInt64() : 0;
                    long uptime = s.TryGetProperty("uptimeMs", out var u) ? u.GetInt64() : 0;
                    string mode = s.TryGetProperty("mode", out var m) ? (m.GetString() ?? "") : "";
                    string decision = s.TryGetProperty("lastDecision", out var d) ? (d.GetString() ?? "") : "";
                    OnState?.Invoke(tick, uptime, mode, decision);
                }
                break;

            case Msg.BrainLifeState:
                OnLifeState?.Invoke(PayloadReader.Read<LifeStateDto>(env.Payload));
                break;

            case Msg.BrainLifeOutputAppend:
                // no noisy info log for life outputs
                OnLifeOutput?.Invoke(PayloadReader.Read<LifeOutputDto>(env.Payload));
                break;

            case Msg.TraceAppend:
                var trace = PayloadReader.Read<TraceDto>(env.Payload);
                OnTrace?.Invoke($"[{DateTime.Now:HH:mm:ss}] {TraceFormatter.FormatCompact(trace)}");
                break;
            }
        }
        catch (Exception ex)
        {
            OnInfo?.Invoke($"HandleIncoming error: {ex.Message}");
        }
    }

    private static string HexHead(byte[] data, int count)
    {
        if (data.Length == 0) return "empty";
        var take = Math.Min(count, data.Length);
        var chars = new char[take * 2];
        for (var i = 0; i < take; i++)
        {
            var b = data[i];
            chars[i * 2] = GetHex(b >> 4);
            chars[i * 2 + 1] = GetHex(b & 0xF);
        }
        return new string(chars);
    }

    private static char GetHex(int v) => (char)(v < 10 ? '0' + v : 'A' + (v - 10));

    private async Task SendAsync(Envelope env, CancellationToken ct)
    {
        if (_stream is null) return;
        await _sendLock.WaitAsync(ct);
        try
        {
            var bytes = JsonWire.Serialize(env);
            await Framing.WriteFrameAsync(_stream, bytes, ct);
        }
        catch (Exception ex)
        {
            OnInfo?.Invoke($"Send error: {ex.Message}");
            await DisconnectAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
    }
    public async Task SubscribeStateAsync()
    {
        if (_stream is null) return;

        var env = new Envelope(Msg.BrainStateSubscribe, Guid.NewGuid().ToString("N"), NowMs(), new { });
        await SendAsync(env, _cts?.Token ?? CancellationToken.None);
    }

    public async Task SubscribeLifeStateAsync()
    {
        if (_stream is null) return;

        var env = new Envelope(Msg.BrainLifeStateSubscribe, Guid.NewGuid().ToString("N"), NowMs(), new { });
        await SendAsync(env, _cts?.Token ?? CancellationToken.None);
    }

    public async Task SubscribeLifeOutputAsync()
    {
        if (_stream is null) return;

        var env = new Envelope(Msg.BrainLifeOutputSubscribe, Guid.NewGuid().ToString("N"), NowMs(), new { });
        await SendAsync(env, _cts?.Token ?? CancellationToken.None);
    }

    public Task BrainStartAsync() => SendTypeAsync(Msg.BrainStart);
    public Task BrainStopAsync() => SendTypeAsync(Msg.BrainStop);
    public Task BrainStepAsync() => SendTypeAsync(Msg.BrainStep);

    private async Task SendTypeAsync(string type)
    {
        if (_stream is null) return;

        var env = new Envelope(type, Guid.NewGuid().ToString("N"), NowMs(), new { });
        await SendAsync(env, _cts?.Token ?? CancellationToken.None);
    }

    public async Task SubscribeTraceAsync()
    {
        if (_stream is null) return;
        var env = new Envelope(Msg.TraceSubscribe, Guid.NewGuid().ToString("N"), NowMs(), new { });
        await SendAsync(env, _cts?.Token ?? CancellationToken.None);
    }

public async Task SetInputAsync(BrainInputDto input)
{
    var env = new Envelope(Msg.InputSet, Guid.NewGuid().ToString("N"), NowMs(), input);
    await SendAsync(env, _cts?.Token ?? CancellationToken.None);
}

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
