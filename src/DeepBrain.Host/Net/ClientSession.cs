using System.Net.Sockets;
using DeepBrain.Shared.Net;

namespace DeepBrain.Host.Net;

public sealed class ClientSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _cts = new();

    public string Remote => _client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    public bool WantsLogs { get; private set; }
    public bool WantsState { get; private set; }

    public ClientSession(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public async Task RunAsync(Func<string, Task> onInfo, CancellationToken serverCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverCt, _cts.Token);
        var ct = linked.Token;

        await onInfo($"Client connected: {Remote}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await Framing.ReadFrameAsync(_stream, maxBytes: 1_000_000, ct);
                if (frame is null) break;

                var env = JsonWire.Deserialize(frame);
                await HandleAsync(env, onInfo, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await onInfo($"Client {Remote} error: {ex.Message}");
        }
        finally
        {
            await onInfo($"Client disconnected: {Remote}");
        }
    }

    private async Task HandleAsync(Envelope env, Func<string, Task> onInfo, CancellationToken ct)
    {
        switch (env.Type)
        {
            case Msg.Ping:
                await SendAsync(new Envelope(Msg.Pong, env.Id, NowMs(), new { ok = true }), ct);
                break;

            case Msg.LogsSubscribe:
                WantsLogs = true;
                await SendAsync(new Envelope(Msg.LogAppend, Guid.NewGuid().ToString("N"), NowMs(),
                    new { text = $"[{DateTime.Now:HH:mm:ss}] logs subscribed ✅" }), ct);
                break;
            case Msg.BrainStateSubscribe:
                WantsState = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] state subscribed 🧠", ct);
                break;

            case Msg.BrainStart:
                await onInfo($"BrainStart from {Remote}");
                // тут пока просто логируем, позже привяжем к BrainLoop
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] brain.start ✅", ct);
                break;

            case Msg.BrainStop:
                await onInfo($"BrainStop from {Remote}");
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] brain.stop 🛑", ct);
                break;

            default:
                await onInfo($"Unknown msg from {Remote}: {env.Type}");
                break;
        }
    }

    public async Task SendLogAsync(string text, CancellationToken ct)
    {
        if (!WantsLogs) return;
        await SendAsync(new Envelope(Msg.LogAppend, Guid.NewGuid().ToString("N"), NowMs(), new { text }), ct);
    }

    public async Task SendAsync(Envelope env, CancellationToken ct)
    {
        var bytes = JsonWire.Serialize(env);
        await Framing.WriteFrameAsync(_stream, bytes, ct);
    }

    public void Stop() => _cts.Cancel();

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        try { _stream.Close(); } catch { }
        try { _client.Close(); } catch { }
        _cts.Dispose();
        await Task.CompletedTask;
    }
public async Task SendStateAsync(object payload, CancellationToken ct)
{
    if (!WantsState) return;
    await SendAsync(new Envelope(Msg.BrainState, Guid.NewGuid().ToString("N"), NowMs(), payload), ct);
}

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
