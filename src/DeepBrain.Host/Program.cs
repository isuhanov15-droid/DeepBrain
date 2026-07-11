using System.Text.Json;
using System.IO;
using DeepBrain.Host.Brain;
using DeepBrain.Host.Brain.Input;
using DeepBrain.Host.BrainLife;
using DeepBrain.Host.ConsoleUi;
using DeepBrain.Host.Infrastructure;
using DeepBrain.Host.Net;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Localization;
using DeepBrain.Shared.Net;
using DeepBrain.Shared.Trace;

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.WriteLine("=== НЕОБРАБОТАННОЕ ИСКЛЮЧЕНИЕ ===");
    Console.WriteLine(e.ExceptionObject?.ToString());
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.WriteLine("=== НЕОБРАБОТАННОЕ ИСКЛЮЧЕНИЕ ЗАДАЧИ ===");
    Console.WriteLine(e.Exception.ToString());
    e.SetObserved();
};

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
TryConfigureConsole();
var consoleOptions = HostConsoleOptions.Parse(args);

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
HostConsoleRuntime.Configure(consoleOptions, ui);
LogLine(consoleLock, logBuffer, logWriter,
    $"Режим консоли: {RussianDisplay.Token(HostConsoleRuntime.Mode.ToString())}, " +
    $"частота панели: {consoleOptions.DashboardFps} кадр/с");
var configPath = HostConfigPathResolver.Resolve(args, AppContext.BaseDirectory);
LogLine(consoleLock, logBuffer, logWriter, $"Конфигурация: {configPath}");
var configLoader = new BrainConfigLoader(configPath, msg => LogLine(consoleLock, logBuffer, logWriter, msg));
var mlCoreAvailable = DeepBrain.Host.BrainLife.Ml.MlCoreAvailability.IsAvailable;
LogLine(consoleLock, logBuffer, logWriter,
    mlCoreAvailable
        ? "ML.Core: найден"
        : "ML.Core: не найден, параметр ml.enable будет принудительно отключён");
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
        traceWriter.AddLine($"[{DateTime.Now:HH:mm:ss}] трассировка [{trace.Tick}] этап={trace.Stage}: {payload}");
        if (Volatile.Read(ref traceEnabled) == 1)
        {
            ui.AddTrace(line);
            RenderScreen(ui, consoleLock);
        }
    },
    (output, ct) =>
    {
        LogLine(consoleLock, logBuffer, logWriter,
            $"[жизнь] ВЫВОД тик={output.Tick} клиентов={server.ClientCount} сообщение={output.Message}");
        server.BroadcastLifeOutputAsync(output, ct).GetAwaiter().GetResult();
    },
    msg => LogLine(consoleLock, logBuffer, logWriter, msg)
);

try
{
    var hostVersion = typeof(BrainEngine).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";
    LogLine(consoleLock, logBuffer, logWriter, $"Запуск DeepBrain.Host v{hostVersion}...");

    await server.StartAsync(cts.Token);

    LogLine(consoleLock, logBuffer, logWriter, "DeepBrain.Host запущен и слушает порт 5555");
    if (!useLifeLoop)
        brain.Start();

    await server.BroadcastLogAsync($"[{DateTime.Now:HH:mm:ss}] DeepBrain.Host доступен на 127.0.0.1:5555 ✅");

    var heartbeatTask = RunHeartbeatAsync(server, ui, () => consoleLock, logBuffer, logWriter, cts.Token);
    var brainTask = useLifeLoop && lifeLoop is not null
        ? lifeLoop.RunAsync(cts.Token)
        : RunBrainLoopAsync(brain, server, () => Volatile.Read(ref traceEnabled) == 1, ui, () => consoleLock, logBuffer, logWriter, traceWriter, cts.Token);
    if (HostConsoleRuntime.Mode != HostConsoleMode.Quiet && CanUseInteractiveConsoleInput())
    {
        _ = Task.Run(() => RunCommandLoop(cts, brain, lifeLoop, configLoader, () => Volatile.Read(ref traceEnabled) == 1, v => Interlocked.Exchange(ref traceEnabled, v), ui, () => consoleLock, logBuffer, logWriter));
    }
    else
    {
        LogLine(consoleLock, logBuffer, logWriter,
            "Командная строка отключена: стандартный ввод работает не в интерактивном режиме");
    }

    await Task.WhenAll(heartbeatTask, brainTask);
}
catch (OperationCanceledException) { }
catch (Exception ex)
{
    LogLine(consoleLock, logBuffer, logWriter, "=== КРИТИЧЕСКАЯ ОШИБКА ОСНОВНОГО ЦИКЛА ===");
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
            // Пульс отображается только в заголовке окна.
            await server.BroadcastLogAsync($"[{DateTime.Now:HH:mm:ss}] Пульс DeepBrain.Host {beat}");
            await Task.Delay(1000, ct);
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        LogLine(consoleLockProvider(), logBuffer, logWriter, "=== СБОЙ ЦИКЛА ПУЛЬСА ===");
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
                traceWriter.AddLine($"[{DateTime.Now:HH:mm:ss}] трассировка [{tr.Tick}] этап={tr.Stage}: {payload}");
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
        LogLine(consoleLockProvider(), logBuffer, logWriter, "=== СБОЙ ЦИКЛА МОЗГА ===");
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
    LogLine(consoleLockProvider(), logBuffer, logWriter,
        "Команды: trace (трассировка), stop/start (остановить/запустить), logs (журнал), " +
        "resetml (сброс ML), resetepisode или episode.reset (сброс эпизода), " +
        "mlstatus, mlconnect, mldisconnect, ml.mode, scenario.list, scenario.set, " +
        "curriculum.mode, curriculum.next, memory.status, memory.recent, memory.search, " +
        "reloadconfig, death, exit");
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
                    LogLine(consoleLockProvider(), logBuffer, logWriter, "Трассировка включена");
                }
                else
                {
                    setTrace(0);
                    LogLine(consoleLockProvider(), logBuffer, logWriter, "Трассировка отключена");
                }
                break;
            case "stop":
                brain.Stop();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "Мозг остановлен");
                break;
            case "start":
                brain.Start();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "Мозг запущен");
                break;
            case "logs":
                DumpLogs(consoleLockProvider(), logBuffer);
                break;
            case "resetml":
                lifeLoop?.ResetMl();
                break;
            case "resetepisode":
                lifeLoop?.RequestEpisodeReset();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "Запрошен сброс эпизода");
                break;
            case "episode.reset":
                lifeLoop?.RequestEpisodeReset();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "Запрошен сброс эпизода");
                break;
            case "mlstatus":
                if (lifeLoop is not null)
                    LogLine(consoleLockProvider(), logBuffer, logWriter, lifeLoop.GetMlStatus());
                break;
            case "mlconnect":
                if (lifeLoop is not null)
                {
                    var ok = lifeLoop.TryConnectMl();
                    LogLine(consoleLockProvider(), logBuffer, logWriter,
                        ok ? "Удалённый ML подключён" : "Не удалось подключить удалённый ML");
                }
                break;
            case "mldisconnect":
                lifeLoop?.DisconnectMl();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "Удалённый ML отключён");
                break;
            case "ml.mode":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    LogLine(consoleLockProvider(), logBuffer, logWriter,
                        "Для ml.mode требуется значение training (обучение) или evaluation (оценка)");
                    break;
                }
                lifeLoop?.SetMlMode(arg);
                LogLine(consoleLockProvider(), logBuffer, logWriter,
                    $"Режим ML установлен: {RussianDisplay.Token(arg)}");
                break;
            case "scenario.list":
                var scenarios = lifeLoop?.ListScenarios() ?? Array.Empty<string>();
                LogLine(consoleLockProvider(), logBuffer, logWriter,
                    $"Сценарии: {string.Join(", ", scenarios.Select(RussianDisplay.Token))}");
                break;
            case "scenario.set":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    LogLine(consoleLockProvider(), logBuffer, logWriter,
                        "Для scenario.set требуется техническое имя сценария");
                    break;
                }
                if (lifeLoop?.TrySetScenario(arg) == true)
                    LogLine(consoleLockProvider(), logBuffer, logWriter,
                        $"Сценарий установлен: {RussianDisplay.Token(arg)}");
                else
                    LogLine(consoleLockProvider(), logBuffer, logWriter,
                        $"Сценарий не найден: {arg}");
                break;
            case "curriculum.mode":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    LogLine(consoleLockProvider(), logBuffer, logWriter,
                        "Для curriculum.mode требуется fixed, round_robin, reward_gated или random_seeded");
                    break;
                }
                lifeLoop?.SetCurriculumMode(arg);
                LogLine(consoleLockProvider(), logBuffer, logWriter,
                    $"Режим учебной программы установлен: {RussianDisplay.Token(arg)}");
                break;
            case "curriculum.next":
                lifeLoop?.NextScenario();
                LogLine(consoleLockProvider(), logBuffer, logWriter,
                    "Учебная программа перешла к следующему сценарию");
                break;
            case "memory.status":
                if (lifeLoop is not null)
                    LogLine(consoleLockProvider(), logBuffer, logWriter, lifeLoop.GetMemoryStatus());
                break;
            case "memory.recent":
            {
                var count = int.TryParse(arg, out var parsed) ? Math.Clamp(parsed, 1, 20) : 5;
                var memories = lifeLoop?.GetRecentMemoryLines(count) ?? Array.Empty<string>();
                if (memories.Count == 0)
                {
                    LogLine(consoleLockProvider(), logBuffer, logWriter, "В долговременной памяти пока нет эпизодов");
                    break;
                }

                foreach (var memory in memories)
                    LogLine(consoleLockProvider(), logBuffer, logWriter, $"ВОСПОМИНАНИЕ: {memory}");
                break;
            }
            case "memory.search":
            {
                if (string.IsNullOrWhiteSpace(arg))
                {
                    LogLine(consoleLockProvider(), logBuffer, logWriter,
                        "Для memory.search требуется сценарий, настроение, причина или имя действия");
                    break;
                }

                var memories = lifeLoop?.SearchMemoryLines(arg, 10) ?? Array.Empty<string>();
                if (memories.Count == 0)
                {
                    LogLine(consoleLockProvider(), logBuffer, logWriter, $"Воспоминания не найдены: {arg}");
                    break;
                }

                foreach (var memory in memories)
                    LogLine(consoleLockProvider(), logBuffer, logWriter, $"ВОСПОМИНАНИЕ: {memory}");
                break;
            }
            case "reloadconfig":
                configLoader.ReloadNow();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "Запрошена перезагрузка конфигурации");
                break;
            case "death":
                brain.Stop();
                LogLine(consoleLockProvider(), logBuffer, logWriter, "Остановка Host...");
                cts.Cancel();
                return;
            case "exit":
                LogLine(consoleLockProvider(), logBuffer, logWriter, "Запрошен выход");
                cts.Cancel();
                return;
            default:
                LogLine(consoleLockProvider(), logBuffer, logWriter, "Неизвестная команда");
                break;
        }

        RenderScreen(ui, consoleLockProvider());
    }
}

static void RenderScreen(ConsoleUiState ui, object consoleLock)
{
    if (HostConsoleRuntime.Mode != HostConsoleMode.Dashboard)
        return;

    if (!HostConsoleRuntime.ShouldRender())
        return;

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
        logWriter.AddLine(message);

        switch (HostConsoleRuntime.Mode)
        {
            case HostConsoleMode.Dashboard:
                HostConsoleRuntime.Ui?.AddLog(message);
                break;
            case HostConsoleMode.Logs:
                Console.WriteLine(message);
                break;
            case HostConsoleMode.Quiet:
                break;
        }
    }
}

static void DumpLogs(object consoleLock, List<string> buffer)
{
    lock (consoleLock)
    {
        Console.WriteLine("=== БУФЕР ЖУРНАЛА ===");
        foreach (var line in buffer)
            Console.WriteLine(line);
        Console.WriteLine("=== КОНЕЦ БУФЕРА ЖУРНАЛА ===");
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
    private readonly List<string> _logs = new(200);
    private readonly List<string> _lastFrame = new();
    private LifeStateDto? _lastState;

    public string Heartbeat { get; set; } = "Пульс: _/\\_";

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

    public void AddLog(string line)
    {
        if (_logs.Count > 0 && string.Equals(_logs[^1], line, StringComparison.Ordinal))
            return;
        if (_logs.Count >= 200) _logs.RemoveAt(0);
        _logs.Add(line);
    }

    public List<string> GetLogSnapshot(int maxLines)
    {
        var take = Math.Min(maxLines, _logs.Count);
        return _logs.Skip(_logs.Count - take).ToList();
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
            "Журнал:"
        };

        var logHeight = Math.Min(5, Math.Max(0, safeHeight - lines.Count - 1));
        lines.AddRange(GetLogSnapshot(logHeight));
        lines.Add("Трассировка:");

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
        File.AppendAllText(
            _filePath,
            $"=== DeepBrain.Host запущен {startTime:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}",
            new System.Text.UTF8Encoding(false));
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


enum HostConsoleMode
{
    Dashboard,
    Logs,
    Quiet
}

sealed class HostConsoleOptions
{
    public HostConsoleMode Mode { get; init; }
    public int DashboardFps { get; init; } = 2;

    public static HostConsoleOptions Parse(string[] args)
    {
        var modeText = Environment.GetEnvironmentVariable("DEEPBRAIN_CONSOLE");
        var fps = 2;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                modeText = args[++i];
                continue;
            }

            if (arg.StartsWith("--console=", StringComparison.OrdinalIgnoreCase))
            {
                modeText = arg["--console=".Length..];
                continue;
            }

            if (string.Equals(arg, "--dashboard-fps", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var parsedFps))
                    fps = parsedFps;
                continue;
            }

            if (arg.StartsWith("--dashboard-fps=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(arg["--dashboard-fps=".Length..], out var parsedFps))
                    fps = parsedFps;
            }
        }

        var fpsEnv = Environment.GetEnvironmentVariable("DEEPBRAIN_DASHBOARD_FPS");
        if (!string.IsNullOrWhiteSpace(fpsEnv) && int.TryParse(fpsEnv, out var envFps))
            fps = envFps;

        var mode = ParseMode(modeText);
        fps = Math.Clamp(fps, 1, 10);

        return new HostConsoleOptions
        {
            Mode = mode,
            DashboardFps = fps
        };
    }

    private static HostConsoleMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Console.IsOutputRedirected ? HostConsoleMode.Logs : HostConsoleMode.Dashboard;

        return value.Trim().ToLowerInvariant() switch
        {
            "dashboard" => HostConsoleMode.Dashboard,
            "dash" => HostConsoleMode.Dashboard,
            "logs" => HostConsoleMode.Logs,
            "log" => HostConsoleMode.Logs,
            "quiet" => HostConsoleMode.Quiet,
            "silent" => HostConsoleMode.Quiet,
            _ => Console.IsOutputRedirected ? HostConsoleMode.Logs : HostConsoleMode.Dashboard
        };
    }
}

static class HostConsoleRuntime
{
    private static long _lastRenderMs;

    public static HostConsoleMode Mode { get; private set; } = HostConsoleMode.Dashboard;
    public static int DashboardMinIntervalMs { get; private set; } = 500;
    public static ConsoleUiState? Ui { get; private set; }

    public static void Configure(HostConsoleOptions options, ConsoleUiState ui)
    {
        Mode = options.Mode;
        Ui = ui;
        DashboardMinIntervalMs = Math.Max(100, 1000 / Math.Max(1, options.DashboardFps));
    }

    public static bool ShouldRender()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastRenderMs);

        if (now - last < DashboardMinIntervalMs)
            return false;

        return Interlocked.CompareExchange(ref _lastRenderMs, now, last) == last;
    }
}
