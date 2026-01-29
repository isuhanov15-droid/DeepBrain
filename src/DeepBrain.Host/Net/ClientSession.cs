using System.Net.Sockets;
using DeepBrain.Shared.Net;
using System.Text.Json;
using DeepBrain.Shared.Input;


namespace DeepBrain.Host.Net;

public sealed class ClientSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<Task> _onBrainStart;
    private readonly Func<Task> _onBrainStop;
    private readonly Func<Task> _onBrainStep;
    private readonly Func<string, Task> _onInfo;
    private readonly Func<BrainInputDto, Task> _onInputSet;
    public string Remote => _client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    public bool WantsLogs { get; private set; }
    public bool WantsState { get; private set; }
    public bool WantsTrace { get; private set; }


    public ClientSession(TcpClient client, Func<string, Task> onInfo, Func<Task> onBrainStart, Func<Task> onBrainStop, Func<Task> onBrainStep, Func<BrainInputDto, Task> onInputSet)
    {
        _client = client;
        _onInfo = onInfo;
        _stream = client.GetStream();
        _onBrainStart = onBrainStart;
        _onBrainStop = onBrainStop;
        _onBrainStep = onBrainStep;
        _onInputSet = onInputSet;
    }


    public async Task RunAsync(Func<string, Task> onInfo, CancellationToken serverCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverCt, _cts.Token);
        var ct = linked.Token;

        await _onInfo($"Client connected: {Remote}");

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
                await _onBrainStart();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] brain.start ✅", ct);
                break;

            case Msg.BrainStop:
                await _onBrainStop();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] brain.stop 🛑", ct);
                break;

            case Msg.BrainStep:
                await _onBrainStep();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] brain.step 👣", ct);
                break;

            case Msg.TraceSubscribe:
                WantsTrace = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] trace subscribed 🔬", ct);
                break;
            case Msg.InputSet:
                {
                    var input = DeepBrain.Shared.Net.PayloadReader.Read<BrainInputDto>(env.Payload);
                    await _onInputSet(input);
                    await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] input.set ✅", ct);
                    break;
                }




            default:
                await _onInfo($"Unknown msg from {Remote}: {env.Type}");
                break;
        }
    }

    static BrainInputDto ReadInput(object? payload)
    {
        if (payload is null)
            throw new InvalidOperationException("input.set payload is null");

        // 1) Если пришёл как JsonElement (часто так и будет)
        if (payload is JsonElement je)
            return je.Deserialize<BrainInputDto>()
                   ?? throw new InvalidOperationException("input.set payload: cannot deserialize BrainInputDto");

        // 2) Если пришёл как строка JSON
        if (payload is string s)
            return JsonSerializer.Deserialize<BrainInputDto>(s)
                   ?? throw new InvalidOperationException("input.set payload: cannot deserialize from string");

        // 3) На всякий случай: если это уже BrainInputDto
        if (payload is BrainInputDto dto)
            return dto;

        // 4) Последний шанс: сериализуем обратно и читаем
        var json = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<BrainInputDto>(json)
               ?? throw new InvalidOperationException("input.set payload: cannot deserialize from object");
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
    public async Task SendTraceAsync(object payload, CancellationToken ct)
    {
        if (!WantsTrace) return;
        await SendAsync(new Envelope(Msg.TraceAppend, Guid.NewGuid().ToString("N"), NowMs(), payload), ct);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
