using DeepBrain.Host.Brain;
using DeepBrain.Host.Net;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var brain = new BrainEngine();

var server = new TcpBrainServer(
    port: 5555,
    onBrainStart: () => { brain.Start(); return Task.CompletedTask; },
    onBrainStop:  () => { brain.Stop();  return Task.CompletedTask; },
    onBrainStep:  () => { brain.Step(isForced: true); return Task.CompletedTask; },
    onInputSet:   dto => { brain.SetInput(dto); return Task.CompletedTask; }
);


server.Start();

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
        await server.BroadcastLogAsync(
            $"[{DateTime.Now:HH:mm:ss}] DeepBrain.Host heartbeat #{i}",
            cts.Token
        );

        await Task.Delay(1000, cts.Token);
    }
}, cts.Token);

// brain/state loop (раз в 250мс)
var brainLoopTask = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        // Тикаем только если running (BrainEngine сам решает)
        var traces = brain.TickWithTrace(isForced: false);

        foreach (var tr in traces)
            await server.BroadcastTraceAsync(tr, cts.Token);

        // Шлём состояние только из BrainEngine (единственный источник правды)
        await server.BroadcastStateAsync(brain.GetState(), cts.Token);

        await Task.Delay(250, cts.Token);
    }
}, cts.Token);


await Task.WhenAll(acceptTask, heartbeatTask, brainLoopTask);
await server.DisposeAsync();
