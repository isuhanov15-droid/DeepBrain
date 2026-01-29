using System;
using System.Threading;
using System.Threading.Tasks;



using System.Net;
using System.Net.Sockets;

namespace DeepBrain.Host.Net;

public sealed class TcpBrainServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly List<ClientSession> _clients = new();
    private readonly object _lock = new();

    private readonly Func<Task> _onBrainStart;
    private readonly Func<Task> _onBrainStop;
    private readonly Func<Task> _onBrainStep;


    public int Port { get; }

    public TcpBrainServer(int port, Func<Task> onBrainStart, Func<Task> onBrainStop, Func<Task> onBrainStep)
{
    Port = port;
    _listener = new TcpListener(IPAddress.Loopback, port); // <-- ВАЖНО

    _onBrainStart = onBrainStart;
    _onBrainStop  = onBrainStop;
    _onBrainStep  = onBrainStep;
}



    public void Start() => _listener.Start();

    public async Task RunAcceptLoopAsync(Func<string, Task> onInfo, CancellationToken ct)
    {
        await onInfo($"Host listening on 127.0.0.1:{Port}");

        while (!ct.IsCancellationRequested)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(ct);
            var session = new ClientSession(
    client,
    onInfo,
    _onBrainStart,
    _onBrainStop,
    _onBrainStep
);


            lock (_lock) _clients.Add(session);

            _ = Task.Run(async () =>
            {
                try
                {
                    await session.RunAsync(onInfo, ct);
                }
                finally
                {
                    lock (_lock) _clients.Remove(session);
                    await session.DisposeAsync();
                }
            }, ct);
        }
    }

    public async Task BroadcastLogAsync(string text, CancellationToken ct)
    {
        List<ClientSession> snapshot;
        lock (_lock) snapshot = _clients.ToList();

        foreach (var c in snapshot)
        {
            try { await c.SendLogAsync(text, ct); }
            catch { /* ������ ����� ����������, �� ������������� */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _listener.Stop(); } catch { }

        List<ClientSession> snapshot;
        lock (_lock) snapshot = _clients.ToList();
        foreach (var c in snapshot)
        {
            try { c.Stop(); await c.DisposeAsync(); } catch { }
        }
    }
    public async Task BroadcastStateAsync(object payload, CancellationToken ct)
    {
        List<ClientSession> snapshot;
        lock (_lock) snapshot = _clients.ToList();

        foreach (var c in snapshot)
        {
            try { await c.SendStateAsync(payload, ct); }
            catch { }
        }
    }
    public async Task BroadcastTraceAsync(object payload, CancellationToken ct)
{
    List<ClientSession> snapshot;
    lock (_lock) snapshot = _clients.ToList();

    foreach (var c in snapshot)
    {
        try { await c.SendTraceAsync(payload, ct); } catch { }
    }
}


}
