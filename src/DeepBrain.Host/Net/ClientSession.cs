using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
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
    private readonly Func<Task> _onLifeStart;
    private readonly Func<Task> _onLifeStop;
    private readonly Func<Task> _onLifeStep;
    private readonly Func<BrainInputDto, Task> _onInputSet;
    private readonly Func<string, Task> _onEventPush;
    private readonly Func<IReadOnlyList<LifeOutputDto>> _getLifeOutputs;
    private readonly Channel<Envelope> _sendQueue = Channel.CreateUnbounded<Envelope>();
    private Task? _sendLoop;

    private bool _wantsLogs;
    private bool _wantsState;
    private bool _wantsTrace;
    private bool _wantsLifeState;
    private bool _wantsLifeOutput;

    public EndPoint Remote => _client.Client.RemoteEndPoint!;

    public ClientSession(
        TcpClient client,
        Func<Task> onBrainStart,
        Func<Task> onBrainStop,
        Func<Task> onBrainStep,
        Func<Task> onLifeStart,
        Func<Task> onLifeStop,
        Func<Task> onLifeStep,
        Func<BrainInputDto, Task> onInputSet,
        Func<string, Task> onEventPush,
        Func<IReadOnlyList<LifeOutputDto>> getLifeOutputs)
    {
        _client = client;
        _stream = client.GetStream();

        _onBrainStart = onBrainStart;
        _onBrainStop = onBrainStop;
        _onBrainStep = onBrainStep;
        _onLifeStart = onLifeStart;
        _onLifeStop = onLifeStop;
        _onLifeStep = onLifeStep;
        _onInputSet = onInputSet;
        _onEventPush = onEventPush;
        _getLifeOutputs = getLifeOutputs;
    }

    public async Task RunAsync(Func<string, Task> onInfo, CancellationToken ct)
    {
        await onInfo($"+ клиент {Remote}");
        _sendLoop = Task.Run(() => SendLoopAsync(onInfo, ct), ct);

        while (!ct.IsCancellationRequested)
        {
            Envelope? env;
            try
            {
                env = await Framing.ReadEnvelopeAsync(_stream, Framing.MaxFrameBytes, ct);
            }
            catch (Exception ex)
            {
                await onInfo($"Ошибка чтения сообщения от {Remote}: {ex.Message}");
                break;
            }

            if (env is null) break;

            await HandleAsync(env, onInfo, ct);
        }

        _sendQueue.Writer.TryComplete();
        if (_sendLoop is not null)
            await _sendLoop;
        await onInfo($"- клиент {Remote}");
    }

    private async Task HandleAsync(Envelope env, Func<string, Task> onInfo, CancellationToken ct)
    {
        switch (env.Type)
        {
            case Msg.Ping:
                await EnqueueAsync(new Envelope(Msg.Pong, Array.Empty<byte>()), ct);
                break;

            case Msg.LogsSubscribe:
                _wantsLogs = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Подписка на журнал оформлена ✅", ct);
                break;

            case Msg.BrainStateSubscribe:
                _wantsState = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Подписка на состояние оформлена ✅", ct);
                break;

            case Msg.TraceSubscribe:
                _wantsTrace = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Подписка на трассировку оформлена ✅", ct);
                break;

            case Msg.BrainLifeStateSubscribe:
                _wantsLifeState = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Подписка на жизненное состояние оформлена ✅", ct);
                break;

            case Msg.BrainLifeOutputSubscribe:
                _wantsLifeOutput = true;
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Подписка на жизненный вывод оформлена ✅", ct);
                foreach (var output in _getLifeOutputs())
                {
                    await SendLifeOutputAsync(output, ct);
                }
                break;

            case Msg.BrainStart:
                await _onBrainStart();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Мозг запущен (brain.start) ✅", ct);
                break;

            case Msg.BrainStop:
                await _onBrainStop();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Мозг остановлен (brain.stop) ✅", ct);
                break;

            case Msg.BrainStep:
                await _onBrainStep();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Выполнен шаг мозга (brain.step) ✅", ct);
                break;

            case Msg.BrainLifeStart:
                await _onLifeStart();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Жизненный цикл запущен (brain.life.start) ✅", ct);
                break;

            case Msg.BrainLifeStop:
                await _onLifeStop();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Жизненный цикл остановлен (brain.life.stop) ✅", ct);
                break;

            case Msg.BrainLifeStep:
                await _onLifeStep();
                await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Выполнен шаг жизненного цикла (brain.life.step) ✅", ct);
                break;

            case Msg.InputSet:
                {
                    var input = PayloadReader.Read<BrainInputDto>(env.Payload);
                    await _onInputSet(input);
                    await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Входные данные установлены (input.set) ✅", ct);
                    break;
                }

            case Msg.EventPush:
                {
                    var ev = PayloadReader.Read<BrainEventDto>(env.Payload);
                    await _onEventPush(ev.Name);
                    await SendLogAsync($"[{DateTime.Now:HH:mm:ss}] Событие добавлено (event.push): '{ev.Name}' ✅", ct);
                    break;
                }

            default:
                await onInfo($"Неизвестное сообщение от {Remote}: {env.Type}");
                break;
        }
    }

    public async Task SendLogAsync(string line, CancellationToken ct)
    {
        if (!_wantsLogs) return;
        var payload = PayloadWriter.Write(new LogAppendDto(line));
        await EnqueueAsync(new Envelope(Msg.LogAppend, payload), ct);
    }

    public async Task SendStateAsync(BrainStateDto state, CancellationToken ct)
    {
        if (!_wantsState) return;
        var payload = PayloadWriter.Write(state);
        await EnqueueAsync(new Envelope(Msg.BrainState, payload), ct);
    }

    public async Task SendLifeStateAsync(LifeStateDto state, CancellationToken ct)
    {
        if (!_wantsLifeState) return;
        var payload = PayloadWriter.Write(state);
        await EnqueueAsync(new Envelope(Msg.BrainLifeState, payload), ct);
    }

    public async Task SendLifeOutputAsync(LifeOutputDto output, CancellationToken ct)
    {
        if (!_wantsLifeOutput) return;
        var payload = PayloadWriter.Write(output);
        await EnqueueAsync(new Envelope(Msg.BrainLifeOutputAppend, payload), ct);
    }

    public async Task SendTraceAsync(TraceDto trace, CancellationToken ct)
    {
        if (!_wantsTrace) return;
        var payload = PayloadWriter.Write(trace);
        await EnqueueAsync(new Envelope(Msg.TraceAppend, payload), ct);
    }

    private Task EnqueueAsync(Envelope env, CancellationToken ct)
    {
        _sendQueue.Writer.TryWrite(env);
        return Task.CompletedTask;
    }

    private async Task SendLoopAsync(Func<string, Task> onInfo, CancellationToken ct)
    {
        try
        {
            await foreach (var env in _sendQueue.Reader.ReadAllAsync(ct))
            {
                await Framing.WriteEnvelopeAsync(_stream, env, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            try { await onInfo($"Ошибка отправки данных клиенту {Remote}: {ex.Message}"); } catch { }
        }
    }

    public ValueTask DisposeAsync()
    {
        _sendQueue.Writer.TryComplete();
        try { _stream.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}
