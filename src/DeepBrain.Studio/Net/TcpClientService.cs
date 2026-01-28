using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using DeepBrain.Shared.Net;
using System.Text.Json;

namespace DeepBrain.Studio.Net;

public sealed class TcpClientService : IAsyncDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    public bool IsConnected => _client?.Connected == true;

    public event Action<string>? OnLog;
    public event Action<string>? OnInfo;
    public event Action<long, long, string, string>? OnState;

    public async Task ConnectAsync(string host, int port)
    {
        _cts = new CancellationTokenSource();
        _client = new TcpClient();
        await _client.ConnectAsync(host, port);
        _stream = _client.GetStream();

        OnInfo?.Invoke($"Connected to {host}:{port}");

        _ = Task.Run(() => ReadLoopAsync(_cts.Token));
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

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var frame = await Framing.ReadFrameAsync(_stream, 1_000_000, ct);
                if (frame is null) break;

                var env = JsonWire.Deserialize(frame);
                HandleIncoming(env);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnInfo?.Invoke($"ReadLoop error: {ex.Message}");
        }
        finally
        {
            OnInfo?.Invoke("Disconnected.");
        }
    }



    private void HandleIncoming(Envelope env)
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

        }
    }


    private async Task SendAsync(Envelope env, CancellationToken ct)
    {
        if (_stream is null) return;
        var bytes = JsonWire.Serialize(env);
        await Framing.WriteFrameAsync(_stream, bytes, ct);
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


    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
