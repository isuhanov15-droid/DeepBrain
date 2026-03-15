using System.Text.Json;
using System.IO;
using DeepBrain.Host.Brain;
using DeepBrain.Host.Brain.Input;
using DeepBrain.Host.BrainLife;
using DeepBrain.Host.ConsoleUi;
using DeepBrain.Host.Infrastructure;
using DeepBrain.Host.Net;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Net;
using DeepBrain.Shared.Trace;

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
TryConfigureConsole();

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
var mlCoreAvailable = DeepBrain.Host.BrainLife.Ml.MlCoreAvailability.IsAvailable;
LogLine(consoleLock, logBuffer, logWriter, mlCoreAvailable ? "ML.Core: found" : "ML.Core: NOT found, ml.enable forced false if missing");
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
    (state, ct) =>
    {
        server.BroadcastLifeStateAsync(state, ct).GetAwaiter().GetResult();
        ui.UpdateState(state);
        RenderScreen(ui, consoleLock);
    },
    (trace, ct) =>
    {
        server.BroadcastTraceAsync(trace, ct).GetAwaiter().GetResult();
        var payload = JsonSerializer.Serialize(trace.Data, JsonWire.Options);
        var line = TraceFormatter.FormatCompact(trace);
        traceWriter.AddLine($"[{DateTime.Now:HH:mm:ss}] trace [{trace.Tick}] {trace.Stage}: {payload}");
        if (Volatile.Read(ref traceEnabled) == 1)
        {
            ui.AddTrace(line);
            RenderScreen(ui, consoleLock);
        }
    },
    (output, ct) =>
    {
        LogLine(consoleLock, logBuffer, logWriter, $"[life] OUTPUT tick={output.Tick} clients={server.ClientCount} msg={output.Message}");
        server.BroadcastLifeOutputAsync(output, ct).GetAwaiter().GetResult();
    },
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
    if (CanUseInteractiveConsoleInput())
    {
        _ = Task.Run(() => RunCommandLoop(cts, brain, lifeLoop, configLoader, () => Volatile.Read(ref traceEnabled) == 1, v => Interlocked.Exchange(ref traceEnabled, v), ui, () => consoleLock, logBuffer, logWriter));
    }
    else
    {
        LogLine(consoleLock, logBuffer, logWriter, "console command loop disabled: non-interactive input");
    }

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
            TrySetConsoleTitle($"DeepBrain.Host {beat}");
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
                var traceLine = TraceFormatter.FormatCompact(tr);
                traceWriter.AddLine($"[{DateTime.Now:HH:mm:ss}] trace [{tr.Tick}] {tr.Stage}: {payload}");
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
    BrainConfigLoader configLoader,
    Func<bool> traceOn,
    Action<int> setTrace,
    ConsoleUiState ui,
    Func<object> consoleLockProvider,
    List<string> logBuffer,
    FileBatchWriter logWriter)
{
    LogLine(consoleLockProvider(), logBuffer, logWriter, "Commands: trace, stop, start, logs, resetml, resetepisode, episode.reset, mlstatus, mlconnect, mldisconnect, ml.mode, scenario.list, scenario.set, curriculum.mode, curriculum.next, reloadconfig, death, exit");
    while (!cts.IsCancellationRequested)
    {
        var line = Console.ReadLine();
        if (line == null)
            continue;

        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            continue;
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : "";

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
            case "resetepisode":
                lifeLoop?.RequestEpisodeReset();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "episode reset requested");
                break;
            case "episode.reset":
                lifeLoop?.RequestEpisodeReset();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "episode reset requested");
                break;
            case "mlstatus":
                if (lifeLoop is not null)
                    LogLine(consoleLockProvider(), logBuffer, logWriter, lifeLoop.GetMlStatus());
                break;
            case "mlconnect":
                if (lifeLoop is not null)
                {
                    var ok = lifeLoop.TryConnectMl();
                    LogLine(consoleLockProvider(), logBuffer, logWriter, ok ? "ml remote connected" : "ml remote connect failed");
                }
                break;
            case "mldisconnect":
                lifeLoop?.DisconnectMl();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "ml remote disconnected");
                break;
            case "ml.mode":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    LogLine(consoleLockProvider(), logBuffer, logWriter, "ml.mode requires training|evaluation");
                    break;
                }
                lifeLoop?.SetMlMode(arg);
                LogLine(consoleLockProvider(), logBuffer, logWriter, $"ml mode set to {arg}");
                break;
            case "scenario.list":
                var scenarios = lifeLoop?.ListScenarios() ?? Array.Empty<string>();
                LogLine(consoleLockProvider(), logBuffer, logWriter, $"scenarios: {string.Join(", ", scenarios)}");
                break;
            case "scenario.set":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    LogLine(consoleLockProvider(), logBuffer, logWriter, "scenario.set requires <name>");
                    break;
                }
                if (lifeLoop?.TrySetScenario(arg) == true)
                    LogLine(consoleLockProvider(), logBuffer, logWriter, $"scenario set: {arg}");
                else
                    LogLine(consoleLockProvider(), logBuffer, logWriter, $"scenario not found: {arg}");
                break;
            case "curriculum.mode":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    LogLine(consoleLockProvider(), logBuffer, logWriter, "curriculum.mode requires fixed|round_robin|reward_gated|random_seeded");
                    break;
                }
                lifeLoop?.SetCurriculumMode(arg);
                LogLine(consoleLockProvider(), logBuffer, logWriter, $"curriculum mode set to {arg}");
                break;
            case "curriculum.next":
                lifeLoop?.NextScenario();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "curriculum advanced to next scenario");
                break;
            case "reloadconfig":
                configLoader.ReloadNow();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "config reload requested");
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
    if (!CanUseInteractiveConsoleOutput())
        return;

    lock (consoleLock)
    {
        try
        {
            var width = GetConsoleWidthSafe();
            var height = GetConsoleHeightSafe();
            var frame = ui.BuildFrame(width, height);
            if (!ui.TrySetFrame(frame))
                return;

            for (var i = 0; i < frame.Count; i++)
            {
                Console.SetCursorPosition(0, i);
                WritePadded(frame[i], width);
            }

            Console.SetCursorPosition(0, Math.Max(0, Math.Min(frame.Count, height - 1)));
        }
        catch (IOException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }
}

static void WritePadded(string text, int width)
{
    var safeWidth = Math.Max(8, width - 1);
    Console.Write(ConsoleLineFormatter.FitToWidth(text, safeWidth).PadRight(safeWidth));
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

static void TryConfigureConsole()
{
    try
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.InputEncoding = new System.Text.UTF8Encoding(false);
    }
    catch
    {
    }
}

static bool CanUseInteractiveConsoleOutput()
{
    try
    {
        return !Console.IsOutputRedirected;
    }
    catch
    {
        return false;
    }
}

static bool CanUseInteractiveConsoleInput()
{
    try
    {
        return !Console.IsInputRedirected;
    }
    catch
    {
        return false;
    }
}

static int GetConsoleWidthSafe()
{
    try
    {
        return Math.Max(8, Console.WindowWidth);
    }
    catch
    {
        return 120;
    }
}

static int GetConsoleHeightSafe()
{
    try
    {
        return Math.Max(1, Console.WindowHeight);
    }
    catch
    {
        return 24;
    }
}

static void TrySetConsoleTitle(string title)
{
    if (!CanUseInteractiveConsoleOutput())
        return;

    try
    {
        Console.Title = title;
    }
    catch
    {
    }
}

sealed class ConsoleUiState
{
    private readonly List<string> _trace = new(200);
    private readonly List<string> _lastFrame = new();
    private LifeStateDto? _lastState;

    public string Heartbeat { get; set; } = "Heartbeat: _/\\_";

    public void UpdateState(LifeStateDto state)
    {
        _lastState = state;
    }

    public void AddTrace(string line)
    {
        if (_trace.Count > 0 && string.Equals(_trace[^1], line, StringComparison.Ordinal))
            return;
        if (_trace.Count >= 200) _trace.RemoveAt(0);
        _trace.Add(line);
    }

    public List<string> GetTraceSnapshot(int maxLines)
    {
        var take = Math.Min(maxLines, _trace.Count);
        return _trace.Skip(_trace.Count - take).ToList();
    }

    public List<string> BuildFrame(int width, int height)
    {
        var safeHeight = Math.Max(1, height);
        var lines = new List<string>(safeHeight)
        {
            Heartbeat,
            ConsoleLineFormatter.FormatTickLine(_lastState),
            ConsoleLineFormatter.FormatDecisionLine(_lastState),
            ConsoleLineFormatter.FormatRewardLine(_lastState),
            ConsoleLineFormatter.FormatEpisodeLine(_lastState),
            ConsoleLineFormatter.FormatScenarioLine(_lastState),
            "Trace:"
        };

        var traceHeight = Math.Max(0, safeHeight - lines.Count);
        lines.AddRange(GetTraceSnapshot(traceHeight));
        if (lines.Count > safeHeight)
            lines = lines.Take(safeHeight).ToList();

        while (lines.Count < safeHeight)
            lines.Add(string.Empty);

        return lines.Select(line => ConsoleLineFormatter.FitToWidth(line, Math.Max(8, width - 1))).ToList();
    }

    public bool TrySetFrame(List<string> frame)
    {
        if (_lastFrame.Count == frame.Count)
        {
            var changed = false;
            for (var i = 0; i < frame.Count; i++)
            {
                if (!string.Equals(_lastFrame[i], frame[i], StringComparison.Ordinal))
                {
                    changed = true;
                    break;
                }
            }

            if (!changed)
                return false;
        }

        _lastFrame.Clear();
        _lastFrame.AddRange(frame);
        return true;
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
        var dir = HostPaths.GetDataDirectory(baseDir, folderName);
        Directory.CreateDirectory(dir);
        _filePath = HostPaths.GetLogFilePath(baseDir, folderName, filePrefix, startTime);
        File.AppendAllText(_filePath, $"=== DeepBrain.Host started {startTime:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}", new System.Text.UTF8Encoding(false));
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
        File.AppendAllLines(_filePath, _buffer, new System.Text.UTF8Encoding(false));
        _buffer.Clear();
    }
}
