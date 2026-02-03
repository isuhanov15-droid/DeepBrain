using System.Text.Json;
using System.IO;
using DeepBrain.Host.Brain;
using DeepBrain.Host.Brain.Input;
using DeepBrain.Host.BrainLife;
using DeepBrain.Host.Net;
using DeepBrain.Shared.Net;

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.WriteLine("=== UNHANDLED EXCEPTION ===");
    Console.WriteLine(e.ExceptionObject?.ToString());
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.WriteLine("=== UNOBSERVED TASK EXCEPTION ===");
    Console.WriteLine(e.Exception.ToString());
    e.SetObserved();
};

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var inputStore = new InputStore();
var brain = new BrainEngine(inputStore);
var startTime = DateTime.Now;
var logWriter = new FileBatchWriter("Logs", "logs", startTime);
var traceWriter = new FileBatchWriter("Trace", "trace", startTime);
var useLifeLoop = true;
LifeLoop? lifeLoop = null;

var server = new TcpBrainServer(
    port: 5555,
    onBrainStart: () => { brain.Start(); return Task.CompletedTask; },
    onBrainStop: () => { brain.Stop(); return Task.CompletedTask; },
    onBrainStep: () => { brain.TickWithTrace(isForced: true); return Task.CompletedTask; },
    onLifeStart: () => { lifeLoop?.Start(); return Task.CompletedTask; },
    onLifeStop: () => { lifeLoop?.Stop(); return Task.CompletedTask; },
    onLifeStep: () => { lifeLoop?.Step(); return Task.CompletedTask; },
    onInputSet: (dto) => { inputStore.Set(dto); return Task.CompletedTask; },
    onEventPush: (name) => { brain.EnqueueEvent(name); return Task.CompletedTask; }
);

var traceEnabled = 0;
var consoleLock = new object();
var logBuffer = new List<string>(500);
var ui = new ConsoleUiState();
var configPath = Path.Combine(AppContext.BaseDirectory, "brainconfig.json");
var configLoader = new BrainConfigLoader(configPath, msg => LogLine(consoleLock, logBuffer, logWriter, msg));
lifeLoop = new LifeLoop(
    new WorldSim(seed: 1337),
    new HomeostasisEngine(),
    new InstinctEngine(),
    new EmotionEngine(),
    new ActionSelector(),
    new Actuator(),
    new RewardEngine(),
    new LearningEngine(),
    configLoader,
    (state, ct) => server.BroadcastLifeStateAsync(state, ct).GetAwaiter().GetResult(),
    (trace, ct) =>
    {
        server.BroadcastTraceAsync(trace, ct).GetAwaiter().GetResult();
        var payload = JsonSerializer.Serialize(trace.Data, JsonWire.Options);
        var line = $"[{DateTime.Now:HH:mm:ss}] life trace [{trace.Tick}] {trace.Stage}: {payload}";
        traceWriter.AddLine(line);
        if (Volatile.Read(ref traceEnabled) == 1)
        {
            ui.AddTrace(line);
            RenderScreen(ui, consoleLock);
        }
    },
    (output, ct) => server.BroadcastLifeOutputAsync(output, ct).GetAwaiter().GetResult(),
    msg => LogLine(consoleLock, logBuffer, logWriter, msg)
);

try
{
    LogLine(consoleLock, logBuffer, logWriter, "Host booting...");

    await server.StartAsync(cts.Token);

    LogLine(consoleLock, logBuffer, logWriter, "Host started OK listening on port 5555");
    if (!useLifeLoop)
        brain.Start();

    await server.BroadcastLogAsync($"[{DateTime.Now:HH:mm:ss}] DeepBrain.Host up on 127.0.0.1:5555 OK");

    var heartbeatTask = RunHeartbeatAsync(server, ui, () => consoleLock, logBuffer, logWriter, cts.Token);
    var brainTask = useLifeLoop && lifeLoop is not null
        ? lifeLoop.RunAsync(cts.Token)
        : RunBrainLoopAsync(brain, server, () => Volatile.Read(ref traceEnabled) == 1, ui, () => consoleLock, logBuffer, logWriter, traceWriter, cts.Token);
    _ = Task.Run(() => RunCommandLoop(cts, brain, lifeLoop, () => Volatile.Read(ref traceEnabled) == 1, v => Interlocked.Exchange(ref traceEnabled, v), ui, () => consoleLock, logBuffer, logWriter));

    await Task.WhenAll(heartbeatTask, brainTask);
}
catch (OperationCanceledException) { }
catch (Exception ex)
{
    LogLine(consoleLock, logBuffer, logWriter, "=== FATAL ERROR IN MAIN ===");
    LogLine(consoleLock, logBuffer, logWriter, ex.ToString());
}
finally
{
    logWriter.Flush();
    traceWriter.Flush();
    await server.DisposeAsync();
}

static async Task RunHeartbeatAsync(TcpBrainServer server, ConsoleUiState ui, Func<object> consoleLockProvider, List<string> logBuffer, FileBatchWriter logWriter, CancellationToken ct)
{
    try
    {
        var pulse = new[] { "_/\\_", "__/\\", "_/\\_", "/\\__" };
        var idx = 0;
        while (!ct.IsCancellationRequested)
        {
            var beat = pulse[idx++ % pulse.Length];
            Console.Title = $"DeepBrain.Host {beat}";
            ui.Heartbeat = "";
            RenderScreen(ui, consoleLockProvider());
            // heartbeat only in title
            await server.BroadcastLogAsync($"[{DateTime.Now:HH:mm:ss}] DeepBrain.Host heartbeat {beat}");
            await Task.Delay(1000, ct);
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        LogLine(consoleLockProvider(), logBuffer, logWriter, "=== HEARTBEAT LOOP CRASH ===");
        LogLine(consoleLockProvider(), logBuffer, logWriter, ex.ToString());
    }
}

static async Task RunBrainLoopAsync(
    BrainEngine brain,
    TcpBrainServer server,
    Func<bool> traceOn,
    ConsoleUiState ui,
    Func<object> consoleLockProvider,
    List<string> logBuffer,
    FileBatchWriter logWriter,
    FileBatchWriter traceWriter,
    CancellationToken ct)
{
    try
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        while (!ct.IsCancellationRequested)
        {
            var traces = brain.TickWithTrace(isForced: false);

            foreach (var tr in traces)
            {
                await server.BroadcastTraceAsync(tr, ct);
                var payload = JsonSerializer.Serialize(tr.Data, jsonOptions);
                var traceLine = $"[{DateTime.Now:HH:mm:ss}] trace [{tr.Tick}] {tr.Stage}: {payload}";
                traceWriter.AddLine(traceLine);
                if (traceOn())
                {
                    ui.AddTrace(traceLine);
                    RenderScreen(ui, consoleLockProvider());
                }
            }

            await server.BroadcastStateAsync(brain.GetState(), ct);

            await Task.Delay(250, ct);
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        LogLine(consoleLockProvider(), logBuffer, logWriter, "=== BRAIN LOOP CRASH ===");
        LogLine(consoleLockProvider(), logBuffer, logWriter, ex.ToString());
    }
}

static void RunCommandLoop(
    CancellationTokenSource cts,
    BrainEngine brain,
    LifeLoop? lifeLoop,
    Func<bool> traceOn,
    Action<int> setTrace,
    ConsoleUiState ui,
    Func<object> consoleLockProvider,
    List<string> logBuffer,
    FileBatchWriter logWriter)
{
    LogLine(consoleLockProvider(), logBuffer, logWriter, "Commands: trace, stop, start, logs, resetml, death, exit");
    while (!cts.IsCancellationRequested)
    {
        var line = Console.ReadLine();
        if (line == null)
            continue;

        var cmd = line.Trim().ToLowerInvariant();
        if (cmd.Length == 0)
            continue;

        switch (cmd)
        {
            case "trace":
                if (!traceOn())
                {
                    setTrace(1);
                    LogLine(consoleLockProvider(), logBuffer, logWriter, "trace enabled");
                }
                else
                {
                    setTrace(0);
                    LogLine(consoleLockProvider(), logBuffer, logWriter, "trace disabled");
                }
                break;
            case "stop":
                brain.Stop();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "brain stopped");
                break;
            case "start":
                brain.Start();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "brain started");
                break;
            case "logs":
                DumpLogs(consoleLockProvider(), logBuffer);
                break;
            case "resetml":
                lifeLoop?.ResetMl();
                break;
            case "death":
                brain.Stop();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "host stopping...");
                cts.Cancel();
                return;
            case "exit":
                LogLine(consoleLockProvider(), logBuffer, logWriter, "exit requested");
                cts.Cancel();
                return;
            default:
                LogLine(consoleLockProvider(), logBuffer, logWriter, "unknown command");
                break;
        }

        RenderScreen(ui, consoleLockProvider());
    }
}

static void RenderScreen(ConsoleUiState ui, object consoleLock)
{
    lock (consoleLock)
    {
        var left = Console.CursorLeft;
        var top = Console.CursorTop;

        var width = Math.Max(10, Console.WindowWidth);
        var height = Math.Max(6, Console.WindowHeight);
        var traceHeight = Math.Max(3, Math.Min(10, height - 4));

        Console.SetCursorPosition(0, 0);
        WritePadded(ui.Heartbeat, width);

        Console.SetCursorPosition(0, 1);
        WritePadded("Trace:", width);

        var lines = ui.GetTraceSnapshot(traceHeight);
        for (var i = 0; i < traceHeight; i++)
        {
            Console.SetCursorPosition(0, 2 + i);
            var line = i < lines.Count ? lines[i] : "";
            WritePadded(line, width);
        }

        Console.SetCursorPosition(0, Math.Max(0, height - 1));
    }
}

static void WritePadded(string text, int width)
{
    if (text.Length > width - 1)
        text = text[..(width - 1)];
    Console.Write(text.PadRight(width - 1));
}

static void LogLine(object consoleLock, List<string> buffer, FileBatchWriter logWriter, string message)
{
    lock (consoleLock)
    {
        if (buffer.Count >= 500) buffer.RemoveAt(0);
        buffer.Add(message);
        Console.WriteLine(message);
        logWriter.AddLine(message);
    }
}

static void DumpLogs(object consoleLock, List<string> buffer)
{
    lock (consoleLock)
    {
        Console.WriteLine("=== LOG BUFFER ===");
        foreach (var line in buffer)
            Console.WriteLine(line);
        Console.WriteLine("=== END LOG BUFFER ===");
    }
}

sealed class ConsoleUiState
{
    private readonly List<string> _trace = new(200);

    public string Heartbeat { get; set; } = "Heartbeat: _/\\_";

    public void AddTrace(string line)
    {
        if (_trace.Count >= 200) _trace.RemoveAt(0);
        _trace.Add(line);
    }

    public List<string> GetTraceSnapshot(int maxLines)
    {
        var take = Math.Min(maxLines, _trace.Count);
        return _trace.Skip(_trace.Count - take).ToList();
    }
}

sealed class FileBatchWriter
{
    private readonly object _lock = new();
    private readonly List<string> _buffer = new(1000);
    private readonly string _filePath;

    public FileBatchWriter(string folderName, string filePrefix, DateTime startTime)
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = Path.Combine(baseDir, folderName);
        Directory.CreateDirectory(dir);
        var fileName = $"{filePrefix}-{startTime:yyyyMMdd-HHmmss}.log";
        _filePath = Path.Combine(dir, fileName);
        File.AppendAllText(_filePath, $"=== DeepBrain.Host started {startTime:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
    }

    public void AddLine(string line)
    {
        lock (_lock)
        {
            _buffer.Add(line);
            if (_buffer.Count >= 1000)
            {
                FlushLocked();
            }
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            FlushLocked();
        }
    }

    private void FlushLocked()
    {
        if (_buffer.Count == 0) return;
        File.AppendAllLines(_filePath, _buffer);
        _buffer.Clear();
    }
}
