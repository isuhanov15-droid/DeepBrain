using System.Text.Json;

namespace DeepBrain.Host.BrainLife;

public sealed record MemoryCue(
    string ScenarioName,
    string Mood,
    double Pain,
    double Safety,
    double Arousal
);

public sealed record EpisodicMemoryEntry(
    int SchemaVersion,
    int EpisodeId,
    string ScenarioName,
    DateTimeOffset StartTs,
    DateTimeOffset EndTs,
    int Steps,
    string EndReason,
    double AvgReward,
    double TotalReward,
    Dictionary<string, int> ActionHistogram,
    string DominantMood,
    double AvgPain,
    double AvgSafety,
    double AvgArousal,
    int LoopCount,
    bool ScenarioPassed,
    double Salience
);

public sealed record MemoryMatch(
    EpisodicMemoryEntry Entry,
    double Similarity,
    double Relevance
);

public sealed record MemoryBiasResult(
    IReadOnlyDictionary<string, double> ActionBiases,
    int RecallCount,
    double BestSimilarity,
    int? BestEpisodeId
)
{
    public static MemoryBiasResult Empty { get; } = new(
        new Dictionary<string, double>(StringComparer.Ordinal),
        0,
        0,
        null
    );
}

public sealed record EpisodicMemoryStatus(
    bool Enabled,
    int EpisodeCount,
    int Capacity,
    string Path,
    int InvalidLines,
    long RecallRequests,
    int LastRecallCount,
    double LastBestSimilarity,
    DateTimeOffset? LastStoredAt,
    string? LastError
);

/// <summary>
/// Durable, bounded episodic memory. One compact record is stored per
/// completed episode. Recall is based on scenario, mood and homeostatic
/// proximity; recalled outcomes provide only a bounded action-score bias.
/// </summary>
public sealed class PersistentEpisodicMemory
{
    private const int SchemaVersion = 1;
    private readonly object _gate = new();
    private readonly Action<string> _log;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private readonly List<EpisodicMemoryEntry> _entries = new();

    private MemoryConfig _config = MemoryConfig.Default;
    private string _fullPath = Path.GetFullPath(MemoryConfig.Default.Path);
    private bool _configured;
    private bool _loaded;
    private int _invalidLines;
    private int _linesOnDisk;
    private long _recallRequests;
    private int _lastRecallCount;
    private double _lastBestSimilarity;
    private DateTimeOffset? _lastStoredAt;
    private string? _lastError;

    public PersistentEpisodicMemory(Action<string> log)
    {
        _log = log;
    }

    public void Configure(MemoryConfig? config)
    {
        var normalized = Normalize(config ?? MemoryConfig.Default);
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
                if (!string.Equals(_lastError, ex.Message, StringComparison.Ordinal))
                    _log($"ПАМЯТЬ: некорректный путь хранилища: {ex.Message}");
                _lastError = ex.Message;
            }
            return;
        }

        lock (_gate)
        {
            var pathChanged = !string.Equals(_fullPath, fullPath, StringComparison.Ordinal);
            _config = normalized;
            _configured = true;

            if (pathChanged)
            {
                _fullPath = fullPath;
                _loaded = false;
                _entries.Clear();
                _invalidLines = 0;
                _linesOnDisk = 0;
                _lastStoredAt = null;
                _lastError = null;
            }

            if (!_config.Enable)
                return;

            if (_loaded)
            {
                TrimLocked();
                return;
            }

            LoadLocked();
        }
    }

    public EpisodicMemoryEntry? Remember(EpisodeReport report)
    {
        lock (_gate)
        {
            EnsureConfiguredLocked();
            if (!_config.Enable || report.Steps < _config.MinEpisodeSteps)
                return null;

            var entry = CreateEntry(report, _config.RewardScale);
            try
            {
                var directory = Path.GetDirectoryName(_fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(entry, _json);
                File.AppendAllText(_fullPath, json + Environment.NewLine);
                _entries.Add(entry);
                _linesOnDisk++;
                TrimLocked();
                _lastStoredAt = entry.EndTs;
                _lastError = null;

                if (_linesOnDisk > _config.MaxEpisodes * 2)
                    CompactLocked();

                return entry;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _log($"ПАМЯТЬ: не удалось сохранить эпизод: {ex.Message}");
                return null;
            }
        }
    }

    public MemoryBiasResult BuildActionBias(MemoryCue cue)
    {
        lock (_gate)
        {
            EnsureConfiguredLocked();
            _recallRequests++;

            if (!_config.Enable || _entries.Count < _config.MinEpisodesBeforeBias)
            {
                SetLastRecallLocked(MemoryBiasResult.Empty);
                return MemoryBiasResult.Empty;
            }

            var matches = RecallLocked(cue);
            if (matches.Count == 0)
            {
                SetLastRecallLocked(MemoryBiasResult.Empty);
                return MemoryBiasResult.Empty;
            }

            var weightedOutcomes = new Dictionary<string, double>(StringComparer.Ordinal);
            var totalEvidence = matches.Sum(match => match.Relevance);
            var rewardScale = Math.Max(0.000001, _config.RewardScale);

            foreach (var match in matches)
            {
                var totalActions = match.Entry.ActionHistogram.Values.Where(count => count > 0).Sum();
                if (totalActions <= 0)
                    continue;

                var outcome = Math.Tanh(match.Entry.AvgReward / rewardScale);
                if (!match.Entry.ScenarioPassed)
                    outcome = -Math.Max(0.25, Math.Abs(outcome));
                if (match.Entry.LoopCount > 0)
                    outcome -= Math.Min(0.40, match.Entry.LoopCount * 0.10);
                outcome = Math.Clamp(outcome, -1.0, 1.0);

                foreach (var (action, count) in match.Entry.ActionHistogram)
                {
                    if (count <= 0)
                        continue;

                    var share = count / (double)totalActions;
                    var contribution = match.Relevance * outcome * share;
                    weightedOutcomes[action] = weightedOutcomes.TryGetValue(action, out var current)
                        ? current + contribution
                        : contribution;
                }
            }

            var biases = new Dictionary<string, double>(StringComparer.Ordinal);
            if (totalEvidence > 0)
            {
                foreach (var (action, weightedOutcome) in weightedOutcomes)
                {
                    var bias = _config.MaxActionBias * weightedOutcome / totalEvidence;
                    biases[action] = Math.Clamp(bias, -_config.MaxActionBias, _config.MaxActionBias);
                }
            }

            var result = new MemoryBiasResult(
                biases,
                matches.Count,
                matches[0].Similarity,
                matches[0].Entry.EpisodeId
            );
            SetLastRecallLocked(result);
            return result;
        }
    }

    public IReadOnlyList<EpisodicMemoryEntry> GetRecent(int count)
    {
        lock (_gate)
        {
            EnsureConfiguredLocked();
            return _entries
                .OrderByDescending(entry => entry.EndTs)
                .Take(Math.Clamp(count, 1, 50))
                .ToList();
        }
    }

    public IReadOnlyList<EpisodicMemoryEntry> Search(string query, int count)
    {
        var term = query?.Trim() ?? string.Empty;
        if (term.Length == 0)
            return Array.Empty<EpisodicMemoryEntry>();

        lock (_gate)
        {
            EnsureConfiguredLocked();
            return _entries
                .Where(entry =>
                    entry.ScenarioName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    entry.DominantMood.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    entry.EndReason.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    entry.ActionHistogram.Keys.Any(action => action.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(entry => entry.EndTs)
                .Take(Math.Clamp(count, 1, 50))
                .ToList();
        }
    }

    public EpisodicMemoryStatus GetStatus()
    {
        lock (_gate)
        {
            EnsureConfiguredLocked();
            return new EpisodicMemoryStatus(
                _config.Enable,
                _entries.Count,
                _config.MaxEpisodes,
                _fullPath,
                _invalidLines,
                _recallRequests,
                _lastRecallCount,
                _lastBestSimilarity,
                _lastStoredAt,
                _lastError
            );
        }
    }

    private List<MemoryMatch> RecallLocked(MemoryCue cue)
    {
        var now = DateTimeOffset.UtcNow;
        var halfLifeDays = Math.Max(1.0, _config.RecencyHalfLifeDays);

        return _entries
            .Select(entry =>
            {
                var stateDistance =
                    Math.Abs(entry.AvgPain - LifeMath.Clamp01(cue.Pain)) +
                    Math.Abs(entry.AvgSafety - LifeMath.Clamp01(cue.Safety)) +
                    Math.Abs(entry.AvgArousal - LifeMath.Clamp01(cue.Arousal));
                var stateSimilarity = 1.0 - stateDistance / 3.0;
                var scenarioSimilarity = string.Equals(entry.ScenarioName, cue.ScenarioName, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
                var moodSimilarity = string.Equals(entry.DominantMood, cue.Mood, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
                var similarity = Math.Clamp(
                    stateSimilarity * 0.55 + scenarioSimilarity * 0.30 + moodSimilarity * 0.15,
                    0.0,
                    1.0);
                var ageDays = Math.Max(0, (now - entry.EndTs.ToUniversalTime()).TotalDays);
                var recency = Math.Pow(0.5, ageDays / halfLifeDays);
                var relevance = similarity * (0.50 + entry.Salience * 0.50) * recency;
                return new MemoryMatch(entry, similarity, relevance);
            })
            .Where(match => match.Similarity >= _config.MinSimilarity)
            .OrderByDescending(match => match.Relevance)
            .Take(_config.RecallTopK)
            .ToList();
    }

    private void LoadLocked()
    {
        _entries.Clear();
        _invalidLines = 0;
        _linesOnDisk = 0;
        _lastError = null;

        try
        {
            if (File.Exists(_fullPath))
            {
                foreach (var line in File.ReadLines(_fullPath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    _linesOnDisk++;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<EpisodicMemoryEntry>(line, _json);
                        if (entry is null ||
                            entry.SchemaVersion != SchemaVersion ||
                            string.IsNullOrWhiteSpace(entry.ScenarioName) ||
                            string.IsNullOrWhiteSpace(entry.DominantMood) ||
                            string.IsNullOrWhiteSpace(entry.EndReason) ||
                            entry.ActionHistogram is null)
                        {
                            _invalidLines++;
                            continue;
                        }

                        _entries.Add(entry);
                        TrimLocked();
                    }
                    catch (JsonException)
                    {
                        _invalidLines++;
                    }
                }
            }

            _loaded = true;
            _lastStoredAt = _entries.Count > 0 ? _entries[^1].EndTs : null;
            _log($"ПАМЯТЬ: загружено эпизодов={_entries.Count} путь={_fullPath} повреждённых строк={_invalidLines}");
        }
        catch (Exception ex)
        {
            _loaded = true;
            _lastError = ex.Message;
            _log($"ПАМЯТЬ: не удалось загрузить хранилище: {ex.Message}");
        }
    }

    private void CompactLocked()
    {
        var tempPath = _fullPath + ".tmp";
        try
        {
            var lines = _entries.Select(entry => JsonSerializer.Serialize(entry, _json));
            File.WriteAllLines(tempPath, lines);
            File.Move(tempPath, _fullPath, overwrite: true);
            _linesOnDisk = _entries.Count;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _log($"ПАМЯТЬ: не удалось уплотнить хранилище: {ex.Message}");
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // The original store remains authoritative.
            }
        }
    }

    private void TrimLocked()
    {
        var overflow = _entries.Count - _config.MaxEpisodes;
        if (overflow > 0)
            _entries.RemoveRange(0, overflow);
    }

    private void EnsureConfiguredLocked()
    {
        if (_configured)
            return;

        _configured = true;
        _config = Normalize(MemoryConfig.Default);
        _fullPath = Path.GetFullPath(_config.Path);
        if (_config.Enable)
            LoadLocked();
    }

    private void SetLastRecallLocked(MemoryBiasResult result)
    {
        _lastRecallCount = result.RecallCount;
        _lastBestSimilarity = result.BestSimilarity;
    }

    private static EpisodicMemoryEntry CreateEntry(EpisodeReport report, double rewardScale)
    {
        var dominantMood = report.MoodDistribution
            .OrderByDescending(pair => pair.Value)
            .Select(pair => pair.Key)
            .FirstOrDefault() ?? "unknown";
        var scale = Math.Max(0.000001, rewardScale);
        var rewardSignal = Math.Min(1.0, Math.Abs(report.AvgReward) / scale);
        var loopSignal = report.LoopCount > 0 || string.Equals(report.EndReason, "loop", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
        var failureSignal = report.ScenarioScore?.Passed == false ? 1.0 : 0.0;
        var salience = Math.Clamp(0.20 + rewardSignal * 0.50 + loopSignal * 0.20 + failureSignal * 0.10, 0.0, 1.0);

        return new EpisodicMemoryEntry(
            SchemaVersion,
            report.EpisodeId,
            report.ScenarioName,
            report.StartTs,
            report.EndTs,
            report.Steps,
            report.EndReason,
            report.AvgReward,
            report.TotalReward,
            new Dictionary<string, int>(report.ActionHistogram, StringComparer.Ordinal),
            dominantMood,
            report.AvgPain,
            report.AvgSafety,
            report.AvgArousal,
            report.LoopCount,
            report.ScenarioScore?.Passed == true,
            salience
        );
    }

    private static MemoryConfig Normalize(MemoryConfig config)
    {
        return config with
        {
            Path = string.IsNullOrWhiteSpace(config.Path) ? MemoryConfig.Default.Path : config.Path.Trim(),
            MaxEpisodes = Math.Clamp(config.MaxEpisodes, 10, 100_000),
            RecallTopK = Math.Clamp(config.RecallTopK, 1, 100),
            MinSimilarity = Math.Clamp(config.MinSimilarity, 0.0, 1.0),
            MaxActionBias = Math.Clamp(config.MaxActionBias, 0.0, 0.50),
            RecencyHalfLifeDays = Math.Clamp(config.RecencyHalfLifeDays, 1.0, 3650.0),
            MinEpisodesBeforeBias = Math.Clamp(config.MinEpisodesBeforeBias, 1, 10_000),
            MinEpisodeSteps = Math.Clamp(config.MinEpisodeSteps, 1, 1_000_000),
            RewardScale = Math.Clamp(config.RewardScale, 0.000001, 10.0)
        };
    }
}
