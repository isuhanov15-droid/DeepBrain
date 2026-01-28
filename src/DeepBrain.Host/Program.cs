using DeepBrain.Host.Net;
using DeepBrain.Shared.Brain;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var server = new TcpBrainServer(port: 5555);
server.Start();

var startedAt = DateTimeOffset.UtcNow;
long tick = 0;
string mode = "idle";
string lastDecision = "none";

Task Info(string s)
{
    Console.WriteLine(s);
    return Task.CompletedTask;
}

// accept loop
var acceptTask = server.RunAcceptLoopAsync(Info, cts.Token);

// heartbeat logs (раз в 1с)
var heartbeatTask = Task.Run(async () =>
{
    int i = 0;
    while (!cts.Token.IsCancellationRequested)
    {
        i++;
        var text = $"[{DateTime.Now:HH:mm:ss}] DeepBrain.Host heartbeat #{i}";
        await server.BroadcastLogAsync(text, cts.Token);
        await Task.Delay(1000, cts.Token);
    }
}, cts.Token);

// state loop (раз в 250мс)
var stateTask = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        tick++;

        // пока примитивная “логика”, потом заменим BrainLoop
        if (tick % 20 == 0) mode = mode == "idle" ? "running" : "idle";
        lastDecision = (tick % 2 == 0) ? "observe" : "wait";

        var uptimeMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        var dto = new BrainStateDto(tick, uptimeMs, mode, lastDecision);

        await server.BroadcastStateAsync(dto, cts.Token);

        await Task.Delay(250, cts.Token);
    }
}, cts.Token);

await Task.WhenAll(acceptTask, heartbeatTask, stateTask);
await server.DisposeAsync();
