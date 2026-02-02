using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Input;
using DeepBrain.Shared.Trace;

namespace DeepBrain.Host.Net;

public sealed class TcpBrainServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<ClientSession, byte> _sessions = new();

    private readonly Func<Task> _onBrainStart;
    private readonly Func<Task> _onBrainStop;
    private readonly Func<Task> _onBrainStep;
    private readonly Func<Task> _onLifeStart;
    private readonly Func<Task> _onLifeStop;
    private readonly Func<Task> _onLifeStep;
    private readonly Func<BrainInputDto, Task> _onInputSet;
    private readonly Func<string, Task> _onEventPush;

    public TcpBrainServer(
        int port,
        Func<Task> onBrainStart,
        Func<Task> onBrainStop,
        Func<Task> onBrainStep,
        Func<Task> onLifeStart,
        Func<Task> onLifeStop,
        Func<Task> onLifeStep,
        Func<BrainInputDto, Task> onInputSet,
        Func<string, Task> onEventPush)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);

        _onBrainStart = onBrainStart;
        _onBrainStop  = onBrainStop;
        _onBrainStep  = onBrainStep;
        _onLifeStart  = onLifeStart;
        _onLifeStop   = onLifeStop;
        _onLifeStep   = onLifeStep;
        _onInputSet   = onInputSet;
        _onEventPush  = onEventPush;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _listener.Start();
        _ = AcceptLoopAsync(ct);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch when (ct.IsCancellationRequested) { break; }

            var session = new ClientSession(
                client, _onBrainStart, _onBrainStop, _onBrainStep, _onLifeStart, _onLifeStop, _onLifeStep, _onInputSet, _onEventPush);

            _sessions.TryAdd(session, 0);

            _ = Task.Run(async () =>
            {
                try { await session.RunAsync(BroadcastLogAsync, ct); }
                finally
                {
                    _sessions.TryRemove(session, out _);
                    await session.DisposeAsync();
                }
            }, ct);
        }
    }

    public async Task BroadcastLogAsync(string line)
    {
        foreach (var s in _sessions.Keys)
            try { await s.SendLogAsync(line, CancellationToken.None); } catch { }
    }

    public async Task BroadcastStateAsync(BrainStateDto state, CancellationToken ct)
    {
        foreach (var s in _sessions.Keys)
            try { await s.SendStateAsync(state, ct); } catch { }
    }

    public async Task BroadcastLifeStateAsync(LifeStateDto state, CancellationToken ct)
    {
        foreach (var s in _sessions.Keys)
            try { await s.SendLifeStateAsync(state, ct); } catch { }
    }

    public async Task BroadcastTraceAsync(TraceDto trace, CancellationToken ct)
    {
        foreach (var s in _sessions.Keys)
            try { await s.SendTraceAsync(trace, ct); } catch { }
    }

    public ValueTask DisposeAsync()
    {
        try { _listener.Stop(); } catch { }
        foreach (var s in _sessions.Keys)
            try { s.DisposeAsync(); } catch { }
        _sessions.Clear();
        return ValueTask.CompletedTask;
    }
}
