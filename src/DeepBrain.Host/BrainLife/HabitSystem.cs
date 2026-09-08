using System.Text.Json;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V5;

namespace DeepBrain.Host.BrainLife;

public sealed record HabitPersistentState(
    string Id,
    string CueKey,
    string RoutineAction,
    double Strength,
    long CueExposures,
    long RoutineExecutions,
    long PositiveReinforcements,
    long NegativeReinforcements,
    double CueRewardBaseline,
    double RoutineRewardAverage,
    double AdvantageAverage,
    long LastCueTick,
    long LastExecutionTick,
    bool ImportedLegacy
);

public sealed record HabitPersistenceSnapshot(
    int SchemaVersion,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<HabitPersistentState> Habits
)
{
    public long? ClockTick { get; init; }
}

public sealed record HabitSystemStatus(
    bool Enabled,
    bool Loaded,
    bool ImportedLegacySnapshot,
    string Path,
    int HabitCount,
    int DirtyUpdates,
    long SaveCount,
    DateTimeOffset? LastSavedAt,
    string? LastError
)
{
    public long ClockTick { get; init; }
}

/// <summary>
/// Habit/Character v2. Cue exposure and actual routine execution are tracked
/// separately. Strength changes only from the routine's reward advantage over
/// the cue baseline, and the complete state is persisted atomically.
/// </summary>
public sealed class HabitSystem
{
    private const int SchemaVersion = 3;
    private readonly object _gate = new();
    private readonly Action<string> _log;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly Dictionary<string, HabitPersistentState> _habits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<long>> _recentUses = new(StringComparer.Ordinal);

    private HabitConfig _config = HabitConfig.Default with { ImportPath = "" };
    private string _fullPath = Path.GetFullPath(HabitConfig.Default.Path);
    private bool _configured;
    private bool _loaded;
    private bool _importedLegacySnapshot;
    private int _dirtyUpdates;
    private long _saveCount;
    private DateTimeOffset? _lastSavedAt;
    private string? _lastError;
    private long _clockTick;
    private long _clockOffset;
    private long _lastSessionTick;
    private bool _clockChanged;
    private long _lastDecayBucket;

    public HabitSystem(Action<string>? log = null)
    {
        _log = log ?? (_ => { });
        SeedDefaultsLocked();
    }

    public void Configure(HabitConfig? config)
    {
        var normalized = (config ?? HabitConfig.Default).Normalize();
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(normalized.Path);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _configured = true;
                _config = normalized with { Enable = false };
                _lastError = ex.Message;
            }
            _log($"ПРИВЫЧКИ: некорректный путь хранилища: {ex.Message}");
            return;
        }

        lock (_gate)
        {
            var pathChanged = !_configured ||
                              !string.Equals(_fullPath, fullPath, StringComparison.Ordinal);
            _configured = true;
            _config = normalized;

            if (pathChanged)
            {
                _fullPath = fullPath;
                _loaded = false;
                _importedLegacySnapshot = false;
                _dirtyUpdates = 0;
                _lastSavedAt = null;
                _lastError = null;
                _recentUses.Clear();
                _clockTick = _clockOffset = _lastSessionTick = 0;
                _clockChanged = false;
                _lastDecayBucket = 0;
                SeedDefaultsLocked();
            }

            if (!_config.Enable || _loaded)
            {
                ClampAllLocked();
                return;
            }

            LoadOrImportLocked();
        }
    }

    public string ComputeCue(
        AttentionDto attention,
        CircadianDto circadian,
        LoopDetector loop,
        IReadOnlyList<WorldEventDto> eventsList,
        HomeostasisDto homeo)
    {
        lock (_gate)
        {
            if (_configured && !_config.Enable)
                return "none";
        }

        if (attention.Focus1 == "threat" && attention.Intensity > 0.6)
            return "threat_high";
        if (loop.LoopPenalty > 0.5)
            return "loop_warning";

        // Exhaustion remains a hard physiological priority. Moderate fatigue,
        // however, may no longer swallow social and novelty contexts.
        if (homeo.Fatigue > 0.82 || circadian.SleepPressure > 0.82)
            return "fatigue_high";
        if (eventsList.Any(e => e.Type == "social_ping" && e.Salience > 0.3))
            return "evening_social_ping";
        if (homeo.Energy > 0.35 &&
            eventsList.Any(e => (e.Type == "calm_window" || e.Type == "novelty_opportunity") && e.Salience > 0.3))
            return "calm_novelty_window";
        if (homeo.Fatigue > 0.6 || circadian.SleepPressure > 0.6)
            return "fatigue_high";

        return "none";
    }

    public (string? actionName, double strength, string? habitId) Suggest(string cueKey)
    {
        if (cueKey == "none")
            return (null, 0, null);

        lock (_gate)
        {
            if ((_configured && !_config.Enable) || !_habits.TryGetValue(cueKey, out var habit))
                return (null, 0, null);

            var confidence = CausalEvidenceConfidence(habit);
            var evidenceWeight = habit.ImportedLegacy
                ? _config.LegacyEvidenceWeight + (1 - _config.LegacyEvidenceWeight) * confidence
                : 1.0;
            return (habit.RoutineAction, LifeMath.Clamp01(habit.Strength * evidenceWeight), habit.Id);
        }
    }

    public HabitDto? UpdateAfter(string actionName, double reward, string cueKey, long tick)
    {
        lock (_gate)
        {
            if (_configured && !_config.Enable) return null;
            tick = AdvanceClockLocked(tick);
            if (!_habits.TryGetValue(cueKey, out var habit))
                return null;

            reward = double.IsFinite(reward) ? reward : 0;
            var baselineBefore = habit.CueExposures > 0
                ? habit.CueRewardBaseline
                : reward;
            var cueBaseline = Ema(
                baselineBefore,
                reward,
                habit.CueExposures == 0 ? 1 : _config.CueBaselineLearningRate);

            habit = habit with
            {
                CueExposures = habit.CueExposures + 1,
                CueRewardBaseline = cueBaseline,
                LastCueTick = tick
            };

            if (string.Equals(actionName, habit.RoutineAction, StringComparison.Ordinal))
            {
                var advantage = reward - baselineBefore;
                var executions = habit.RoutineExecutions + 1;
                var outcomeAlpha = Math.Max(0.01, 1.0 / Math.Min(100, executions));
                var routineAverage = habit.RoutineExecutions == 0
                    ? reward
                    : Ema(habit.RoutineRewardAverage, reward, outcomeAlpha);
                var advantageAverage = habit.RoutineExecutions == 0
                    ? advantage
                    : Ema(habit.AdvantageAverage, advantage, outcomeAlpha);

                var strength = habit.Strength;
                var positive = habit.PositiveReinforcements;
                var negative = habit.NegativeReinforcements;
                if (advantage > _config.NeutralAdvantageBand)
                {
                    strength += _config.RoutineLearningRate *
                                Math.Tanh(advantage / _config.AdvantageScale);
                    positive++;
                }
                else if (advantage < -_config.NeutralAdvantageBand)
                {
                    strength += _config.RoutineLearningRate *
                                Math.Tanh(advantage / _config.AdvantageScale);
                    negative++;
                }

                strength = ClampStrength(habit.Id, strength);
                habit = habit with
                {
                    Strength = strength,
                    RoutineExecutions = executions,
                    PositiveReinforcements = positive,
                    NegativeReinforcements = negative,
                    RoutineRewardAverage = routineAverage,
                    AdvantageAverage = advantageAverage,
                    LastExecutionTick = tick
                };
                RecordUseLocked(habit.Id, tick);
            }

            _habits[cueKey] = habit;
            MarkDirtyLocked();
            return ToDto(habit);
        }
    }

    public IReadOnlyList<HabitDto> GetTopHabits(int k)
    {
        lock (_gate)
        {
            return _habits.Values
                .OrderByDescending(h => h.Strength)
                .Take(Math.Clamp(k, 1, 100))
                .Select(ToDto)
                .ToList();
        }
    }

    public HabitDto? GetHabit(string idOrCue)
    {
        var key = idOrCue?.Trim() ?? "";
        lock (_gate)
        {
            var state = _habits.Values.FirstOrDefault(value =>
                string.Equals(value.Id, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.CueKey, key, StringComparison.OrdinalIgnoreCase));
            return state is null ? null : ToDto(state);
        }
    }

    public double ComputeInfluence(PersonalityDto persona, double habitStrength)
    {
        var baseInfluence = LifeMath.Clamp01(0.2 + habitStrength * 0.6);
        var disciplineFactor = LifeMath.Clamp01(1.0 - persona.DisciplineBaseline * 0.6);
        return LifeMath.Clamp01(baseInfluence * disciplineFactor);
    }

    public double GetSatiationFactor(string habitId, long tick)
    {
        lock (_gate)
        {
            tick = AdvanceClockLocked(tick);
            if (!_recentUses.TryGetValue(habitId, out var queue))
                return 1.0;
            while (queue.Count > 0 && tick - queue.Peek() > _config.SatiationWindowTicks)
                queue.Dequeue();
            return queue.Count > _config.SatiationUseLimit
                ? _config.SatiationFactor
                : 1.0;
        }
    }

    public IReadOnlyList<(HabitDto before, HabitDto after)> ApplyDecay(long tick)
    {
        lock (_gate)
        {
            if (_configured && !_config.Enable)
                return Array.Empty<(HabitDto, HabitDto)>();
            tick = AdvanceClockLocked(tick);
            // Schedule on the persisted clock, including after short restarts.
            if (tick <= 0 || tick / _config.DecayEveryTicks <= _lastDecayBucket)
                return Array.Empty<(HabitDto, HabitDto)>();

            _lastDecayBucket = tick / _config.DecayEveryTicks;
            var changes = new List<(HabitDto before, HabitDto after)>();
            foreach (var key in _habits.Keys.ToList())
            {
                var habit = _habits[key];
                if (tick - habit.LastCueTick <= _config.InactiveDecayAfterTicks)
                    continue;

                var nextStrength = ClampStrength(
                    habit.Id,
                    habit.Strength * (1 - _config.InactiveDecayRate));
                if (Math.Abs(nextStrength - habit.Strength) < 0.000001)
                    continue;

                var after = habit with { Strength = nextStrength };
                _habits[key] = after;
                changes.Add((ToDto(habit), ToDto(after)));
            }

            if (changes.Count > 0)
                MarkDirtyLocked();
            return changes;
        }
    }

    public HabitSystemStatus GetStatus()
    {
        lock (_gate)
        {
            return new HabitSystemStatus(
                !_configured || _config.Enable,
                _loaded,
                _importedLegacySnapshot,
                _fullPath,
                _habits.Count,
                _dirtyUpdates,
                _saveCount,
                _lastSavedAt,
                _lastError) { ClockTick = _clockTick };
        }
    }

    public void AdvanceTime(long tick)
    {
        lock (_gate)
            if (!_configured || _config.Enable) AdvanceClockLocked(tick);
    }

    public void Flush()
    {
        lock (_gate)
            PersistLocked(force: true);
    }

    private void LoadOrImportLocked()
    {
        SeedDefaultsLocked();
        try
        {
            if (File.Exists(_fullPath))
            {
                var json = File.ReadAllText(_fullPath);
                var snapshot = JsonSerializer.Deserialize<HabitPersistenceSnapshot>(json, _json)
                               ?? throw new InvalidDataException("пустой файл привычек");
                if (snapshot.SchemaVersion is not (2 or SchemaVersion))
                    throw new InvalidDataException($"неподдерживаемая схема привычек {snapshot.SchemaVersion}");

                ApplyStatesLocked(snapshot.Habits, importedLegacy: false);
                if (snapshot.SchemaVersion == 2)
                {
                    // V2 mixed timestamps from different host runs; their ages
                    // cannot be recovered. Preserve evidence, restart the grace period.
                    foreach (var key in _habits.Keys.ToList())
                        _habits[key] = _habits[key] with { LastCueTick = 0, LastExecutionTick = 0 };
                    _dirtyUpdates = 1;
                    _log("ПРИВЫЧКИ: миграция v2 → v3; статистика сохранена, возраст неизвестен, отсчёт неактивности начат заново");
                }
                else
                {
                    if (snapshot.ClockTick is not long clock || clock < 0 ||
                        _habits.Values.Any(h => h.LastCueTick > clock || h.LastExecutionTick > clock))
                        throw new InvalidDataException("некорректные часы привычек v3");
                    _clockTick = _clockOffset = clock;
                    _lastDecayBucket = clock / _config.DecayEveryTicks;
                }
                _loaded = true;
                if (_dirtyUpdates > 0) PersistLocked(force: true);
                _lastError = null;
                _log($"ПРИВЫЧКИ: загружено {_habits.Count} записей из {_fullPath}");
                return;
            }

            var importPath = ResolveImportPath(_config.ImportPath);
            if (importPath is not null && File.Exists(importPath))
            {
                ImportLegacyLocked(importPath);
                _importedLegacySnapshot = true;
                _log($"ПРИВЫЧКИ: импортирован живой профиль v1.3.0 из {importPath}");
            }

            _loaded = true;
            _dirtyUpdates = 1;
            PersistLocked(force: true);
        }
        catch (Exception ex)
        {
            PreserveInvalidStoreLocked();
            SeedDefaultsLocked();
            _loaded = true;
            _lastError = ex.Message;
            _dirtyUpdates = 1;
            _log($"ПРИВЫЧКИ: не удалось загрузить состояние, использованы безопасные значения: {ex.Message}");
            PersistLocked(force: true);
        }
    }

    private void ImportLegacyLocked(string importPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(importPath));
        var root = document.RootElement;
        JsonElement habits;
        if (root.TryGetProperty("character", out var character) &&
            character.TryGetProperty("habitsTop", out habits))
        {
        }
        else if (root.TryGetProperty("habitsTop", out habits))
        {
        }
        else if (root.TryGetProperty("habits", out habits))
        {
        }
        else
        {
            throw new InvalidDataException("в снимке v1.3.0 отсутствует habitsTop");
        }

        if (habits.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("habitsTop должен быть массивом");

        foreach (var element in habits.EnumerateArray())
        {
            var cueKey = ReadString(element, "cueKey");
            if (cueKey is null || !_habits.TryGetValue(cueKey, out var current))
                continue;

            var id = ReadString(element, "id");
            var action = ReadString(element, "routineAction");
            if (!string.Equals(id, current.Id, StringComparison.Ordinal) ||
                !string.Equals(action, current.RoutineAction, StringComparison.Ordinal))
                continue;

            var strength = ReadDouble(element, "strength", current.Strength);
            var legacyUses = Math.Max(0, ReadInt64(element, "uses"));
            var legacyAverage = ReadDouble(element, "avgReward", 0);
            _habits[cueKey] = current with
            {
                Strength = ClampStrength(current.Id, strength),
                CueExposures = legacyUses,
                RoutineExecutions = 0,
                CueRewardBaseline = legacyAverage,
                RoutineRewardAverage = 0,
                AdvantageAverage = 0,
                ImportedLegacy = true
            };
        }
    }

    private void ApplyStatesLocked(IEnumerable<HabitPersistentState>? states, bool importedLegacy)
    {
        if (states is null)
            return;

        foreach (var state in states)
        {
            if (state is null ||
                !_habits.TryGetValue(state.CueKey ?? "", out var current) ||
                !string.Equals(state.Id, current.Id, StringComparison.Ordinal) ||
                !string.Equals(state.RoutineAction, current.RoutineAction, StringComparison.Ordinal))
                continue;

            _habits[current.CueKey] = NormalizeState(state with
            {
                ImportedLegacy = importedLegacy || state.ImportedLegacy
            });
        }
        ClampAllLocked();
    }

    private void PersistLocked(bool force)
    {
        if (!_configured || !_config.Enable || !_loaded)
            return;
        if (!force && _dirtyUpdates < _config.SaveEveryUpdates)
            return;
        if (force && _dirtyUpdates == 0 && !_clockChanged && File.Exists(_fullPath))
            return;

        var directory = Path.GetDirectoryName(_fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = _fullPath + ".tmp";
        try
        {
            var snapshot = new HabitPersistenceSnapshot(
                SchemaVersion,
                DateTimeOffset.UtcNow,
                _habits.Values.OrderBy(value => value.CueKey, StringComparer.Ordinal).ToList())
            { ClockTick = _clockTick };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, _json);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _fullPath, overwrite: true);
            _dirtyUpdates = 0;
            _clockChanged = false;
            _saveCount++;
            _lastSavedAt = DateTimeOffset.UtcNow;
            _lastError = null;
        }
        catch (Exception ex)
        {
            TryDelete(temporaryPath);
            _lastError = ex.Message;
            _log($"ПРИВЫЧКИ: не удалось атомарно сохранить состояние: {ex.Message}");
        }
    }

    // Host ticks start again at zero; persisted habit timestamps share this
    // monotonic simulation clock instead. Time while the host is off does not count.
    private long AdvanceClockLocked(long sessionTick)
    {
        sessionTick = Math.Max(0, sessionTick);
        if (sessionTick < _lastSessionTick)
        {
            _clockOffset = _clockTick - sessionTick;
            _recentUses.Clear();
        }
        _lastSessionTick = sessionTick;
        var next = checked(_clockOffset + sessionTick);
        if (next > _clockTick)
        {
            _clockTick = next;
            _clockChanged = true;
        }
        return _clockTick;
    }

    private void MarkDirtyLocked()
    {
        _dirtyUpdates++;
        PersistLocked(force: false);
    }

    private void PreserveInvalidStoreLocked()
    {
        if (!File.Exists(_fullPath))
            return;
        try
        {
            var backup = _fullPath + $".invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(_fullPath, backup, overwrite: false);
            _log($"ПРИВЫЧКИ: повреждённый файл сохранён как {backup}");
        }
        catch
        {
        }
    }

    private void SeedDefaultsLocked()
    {
        _habits.Clear();
        AddLocked("regulate_breathe", "threat_high", "breathe_slow", 0.45);
        AddLocked("rest_recover", "fatigue_high", "rest_short", 0.50);
        AddLocked("check_social", "evening_social_ping", "emit_message", 0.40);
        AddLocked("explore_window", "calm_novelty_window", "explore_signal", 0.35);
        AddLocked("break_loop", "loop_warning", "reframe_negative", 0.40);
    }

    private void AddLocked(string id, string cueKey, string action, double strength)
    {
        _habits[cueKey] = new HabitPersistentState(
            id,
            cueKey,
            action,
            strength,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false);
    }

    private void ClampAllLocked()
    {
        foreach (var key in _habits.Keys.ToList())
            _habits[key] = NormalizeState(_habits[key]);
    }

    private HabitPersistentState NormalizeState(HabitPersistentState state) => state with
    {
        Strength = ClampStrength(state.Id, state.Strength),
        CueExposures = Math.Max(0, state.CueExposures),
        RoutineExecutions = Math.Max(0, state.RoutineExecutions),
        PositiveReinforcements = Math.Max(0, state.PositiveReinforcements),
        NegativeReinforcements = Math.Max(0, state.NegativeReinforcements),
        CueRewardBaseline = FiniteOrZero(state.CueRewardBaseline),
        RoutineRewardAverage = FiniteOrZero(state.RoutineRewardAverage),
        AdvantageAverage = FiniteOrZero(state.AdvantageAverage),
        LastCueTick = Math.Max(0, state.LastCueTick),
        LastExecutionTick = Math.Max(0, state.LastExecutionTick)
    };

    private double ClampStrength(string habitId, double value)
    {
        var floor = IsCriticalSkill(habitId)
            ? _config.CriticalSkillFloor
            : _config.GeneralFloor;
        var ceiling = Math.Max(floor, _config.MaxStrength);
        return Math.Clamp(double.IsFinite(value) ? value : floor, floor, ceiling);
    }

    private static bool IsCriticalSkill(string habitId) =>
        habitId is "regulate_breathe" or "break_loop";

    private static double CausalEvidenceConfidence(HabitPersistentState state) =>
        state.RoutineExecutions <= 0
            ? 0
            : state.RoutineExecutions / (state.RoutineExecutions + 25.0);

    private void RecordUseLocked(string habitId, long tick)
    {
        if (!_recentUses.TryGetValue(habitId, out var queue))
        {
            queue = new Queue<long>();
            _recentUses[habitId] = queue;
        }
        queue.Enqueue(tick);
    }

    private static HabitDto ToDto(HabitPersistentState state)
    {
        var executions = (int)Math.Min(int.MaxValue, state.RoutineExecutions);
        var average = state.RoutineExecutions > 0
            ? state.RoutineRewardAverage
            : state.CueRewardBaseline;
        return new HabitDto(
            state.Id,
            state.CueKey,
            state.RoutineAction,
            state.Strength,
            executions,
            average)
        {
            CueExposures = state.CueExposures,
            RoutineExecutions = state.RoutineExecutions,
            PositiveReinforcements = state.PositiveReinforcements,
            NegativeReinforcements = state.NegativeReinforcements,
            CueRewardBaseline = state.CueRewardBaseline,
            RoutineRewardAverage = state.RoutineRewardAverage,
            AdvantageAverage = state.AdvantageAverage,
            LastCueTick = state.LastCueTick,
            LastExecutionTick = state.LastExecutionTick,
            ImportedLegacy = state.ImportedLegacy
        };
    }

    private static string? ResolveImportPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;
        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        var current = Path.GetFullPath(configuredPath);
        if (File.Exists(current))
            return current;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double ReadDouble(JsonElement element, string name, double fallback) =>
        element.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetDouble(out var value) &&
        double.IsFinite(value)
            ? value
            : fallback;

    private static long ReadInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt64(out var value)
            ? value
            : 0;

    private static double Ema(double current, double sample, double alpha) =>
        FiniteOrZero(current) * (1 - alpha) + FiniteOrZero(sample) * alpha;

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
