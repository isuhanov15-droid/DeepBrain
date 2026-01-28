using DeepBrain.Host.Net;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var server = new TcpBrainServer(port: 5555);
server.Start();

Task Info(string s)
{
    Console.WriteLine(s);
    return Task.CompletedTask;
}

// accept loop
var acceptTask = server.RunAcceptLoopAsync(Info, cts.Token);

// heartbeat logs
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

await Task.WhenAll(acceptTask, heartbeatTask);
await server.DisposeAsync();
