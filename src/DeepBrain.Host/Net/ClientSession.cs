using System.Net;
using System.Net.Sockets;
using DeepBrain.Shared.Net;
using DeepBrain.Shared.Trace;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Input;

namespace DeepBrain.Host.Net;

public sealed class ClientSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private readonly Func<Task> _onBrainStart;
    private readonly Func<Task> _onBrainStop;
    private readonly Func<Task> _onBrainStep;
    private readonly Func<BrainInputDto, Task> _onInputSet;
    private readonly Func<string, Task> _onEventPush;

    private bool _wantsLogs;
    private bool _wantsState;
    private bool _wantsTrace;

    public EndPoint Remote => _client.Client.RemoteEndPoint!;

    public ClientSession(
        TcpClient client,
        Func<Task> onBrainStart,
        Func<Task> onBrainStop,
        Func<Task> onBrainStep,
        Func<BrainInputDto, Task> onInputSet,
        Func<string, Task> onEventPush)
    {
        _client = client;
        _stream = client.GetStream();

        _onBrainStart = onBrainStart;
        _onBrainStop = onBrainStop;
        _onBrainStep = onBrainStep;
        _onInputSet = onInputSet;
        _onEventPush = onEventPush;
    }

    public async Task RunAsync(Func<string, Task> onInfo, CancellationToken ct)
    {
        await onInfo($"+ client {Remote}");

        while (!ct.IsCancellationRequested)
        {
            const int MaxEnvelopeBytes = 1_000_000;
            Envelope? env;
            try
            {
                env = await Framing.ReadEnvelopeAsync(_stream, MaxEnvelopeBytes, ct);
            }
            catch (Exception ex)
            {
                await onInfo($"ReadEnvelope error from {Remote}: {ex.Message}");
                break;
            }

            if (env is null) break;

            await HandleAsync(env, onInfo, ct);
        }

        await onInfo($"- client {Remote}");
    }

    private async Task HandleAsync(Envelope env, Func<string, Task> onInfo, CancellationToken ct)
    {
        switch (env.Type)
        {
            case Msg.Ping:
                await SendAsync(new Envelope(Msg.Pong, Array.Empty<byte>()), ct);
                break;

            case Msg.LogsSubscribe:
                _wantsLogs = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] logs subscribed ✅", ct);
                break;

            case Msg.BrainStateSubscribe:
                _wantsState = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] state subscribed ✅", ct);
                break;

            case Msg.TraceSubscribe:
                _wantsTrace = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] trace subscribed ✅", ct);
                break;

            case Msg.BrainStart:
                await _onBrainStart();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] brain.start ✅", ct);
                break;

            case Msg.BrainStop:
                await _onBrainStop();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] brain.stop ✅", ct);
                break;

            case Msg.BrainStep:
                await _onBrainStep();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] brain.step ✅", ct);
                break;

            case Msg.InputSet:
                {
                    var input = PayloadReader.Read<BrainInputDto>(env.Payload);
                    await _onInputSet(input);
                    await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] input.set ✅", ct);
                    break;
                }

            case Msg.EventPush:
                {
                    var ev = PayloadReader.Read<BrainEventDto>(env.Payload);
                    await _onEventPush(ev.Name);
                    await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] event.push '{ev.Name}' ✅", ct);
                    break;
                }

            default:
                await onInfo($"Unknown msg from {Remote}: {env.Type}");
                break;
        }
    }

    public async Task SendLogAsync(string line, CancellationToken ct)
    {
        if (!_wantsLogs) return;
        var payload = PayloadWriter.Write(new LogAppendDto(line));
        await SendAsync(new Envelope(Msg.LogAppend, payload), ct);
    }

    public async Task SendStateAsync(BrainStateDto state, CancellationToken ct)
    {
        if (!_wantsState) return;
        var payload = PayloadWriter.Write(state);
        await SendAsync(new Envelope(Msg.BrainState, payload), ct);
    }

    public async Task SendTraceAsync(TraceDto trace, CancellationToken ct)
    {
        if (!_wantsTrace) return;
        var payload = PayloadWriter.Write(trace);
        await SendAsync(new Envelope(Msg.TraceAppend, payload), ct);
    }

    private Task SendAsync(Envelope env, CancellationToken ct) => Framing.WriteEnvelopeAsync(_stream, env, ct);

    public ValueTask DisposeAsync()
    {
        try { _stream.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}
