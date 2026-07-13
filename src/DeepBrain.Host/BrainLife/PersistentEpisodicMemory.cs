using System.Diagnostics;
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
)
{
    // Optional v2 fields keep every v1 JSONL line readable without migration.
    public int Occurrences { get; init; } = 1;
    public double Strength { get; init; }
    public DateTimeOffset? LastReinforcedAt { get; init; }
    public Dictionary<string, double> ActionRewardAverages { get; init; } = new(StringComparer.Ordinal);
    public string Fingerprint { get; init; } = "";
}

public sealed record MemoryMatch(
    EpisodicMemoryEntry Entry,
    double Similarity,
    double Relevance
);

public sealed record ScenarioExperience(
    string ScenarioName,
    int EpisodeCount,
    int SuccessfulEpisodes,
    double SuccessRate,
    double AvgReward,
    double AvgLoopCount,
    Dictionary<string, double> ActionOutcomes,
    Dictionary<string, double> ActionEvidence,
    string? HelpfulAction,
    string? HarmfulAction,
    double Confidence,
    bool IsMature,
    DateTimeOffset LastUpdatedAt
);

public sealed record MemoryEvidence(
    int EpisodeId,
    int Occurrences,
    double Similarity,
    double Relevance,
    double AvgReward,
    bool ScenarioPassed,
    string? HelpfulAction
);

public sealed record MemoryExplanation(
    MemoryCue Cue,
    int IndexedCandidates,
    int RecalledEpisodes,
    double BestSimilarity,
    string? HelpfulAction,
    double HelpfulScore,
    string? HarmfulAction,
    double HarmfulScore,
    double Confidence,
    ScenarioExperience? ScenarioExperience,
    IReadOnlyList<MemoryEvidence> Evidence
);

public sealed record MemoryConsolidationResult(
    int EntriesBefore,
    int EntriesAfter,
    int RepresentedEpisodes,
    int MergedEntries,
    int ForgottenEntries,
    int ScenarioExperiences,
    long DurationMs
);

public sealed record MemoryStatistics(
    int EntryCount,
    int RepresentedEpisodes,
    int ScenarioCount,
    int MatureScenarioCount,
    int IndexBuckets,
    int LastCandidateCount,
    long DuplicateMerges,
    long ForgottenEntries,
    long ConsolidationRuns,
    double AverageSalience,
    double AverageStrength,
    string ExperiencePath
);

public sealed record MemoryBiasResult(
    IReadOnlyDictionary<string, double> ActionBiases,
    int RecallCount,
    double BestSimilarity,
    int? BestEpisodeId
)
{
    public int IndexedCandidateCount { get; init; }
    public string? HelpfulAction { get; init; }
    public string? HarmfulAction { get; init; }
    public double Confidence { get; init; }
    public ScenarioExperience? ScenarioExperience { get; init; }

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
)
{
    public int RepresentedEpisodes { get; init; }
    public int ScenarioExperiences { get; init; }
    public int IndexedBuckets { get; init; }
    public int LastCandidateCount { get; init; }
    public long DuplicateMerges { get; init; }
    public long ForgottenEntries { get; init; }
    public long ConsolidationRuns { get; init; }
}

/// <summary>
/// Durable cognitive memory. Raw episode outcomes remain the source of truth;
/// similar outcomes are consolidated into reinforced prototypes and stable
/// scenario experience. Tick-time recall uses an in-memory state index and
/// never scans the JSONL file.
/// </summary>
public sealed class PersistentEpisodicMemory
{
    private const int SchemaVersion = 1;
    private const int StateBucketCount = 5;
    private static readonly int[] NeighborOffsets = [0, -1, 1];
    private readonly object _gate = new();
    private readonly Action<string> _log;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private readonly JsonSerializerOptions _prettyJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly List<EpisodicMemoryEntry> _entries = new();
    private readonly Dictionary<string, List<EpisodicMemoryEntry>> _scenarioIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<EpisodicMemoryEntry>> _stateIndex = new();
    private readonly Dictionary<string, List<EpisodicMemoryEntry>> _scenarioStateIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<EpisodicMemoryEntry>> _moodStateIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<EpisodicMemoryEntry>> _fingerprintIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScenarioExperience> _experiences = new(StringComparer.OrdinalIgnoreCase);

    private MemoryConfig _config = MemoryConfig.Default;
    private string _fullPath = Path.GetFullPath(MemoryConfig.Default.Path);
    private string _experiencePath = Path.GetFullPath(Path.Combine("memory", "experience.json"));
    private bool _configured;
    private bool _loaded;
    private int _invalidLines;
    private int _linesOnDisk;
    private int _representedEpisodes;
    private int _newEpisodesSinceConsolidation;
    private long _recallRequests;
    private int _lastRecallCount;
    private int _lastCandidateCount;
    private double _lastBestSimilarity;
    private DateTimeOffset? _lastStoredAt;
    private string? _lastError;
    private long _duplicateMerges;
    private long _forgottenEntries;
    private long _consolidationRuns;

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
            var configChanged = !_configured || !Equals(_config, normalized);
            var pathChanged = !string.Equals(_fullPath, fullPath, StringComparison.Ordinal);
            _config = normalized;
            _configured = true;

            if (pathChanged)
            {
                _fullPath = fullPath;
                _experiencePath = ResolveExperiencePath(fullPath);
                _loaded = false;
                ClearDerivedLocked(clearEntries: true);
                _invalidLines = 0;
                _linesOnDisk = 0;
                _lastStoredAt = null;
                _lastError = null;
            }

            if (!_config.Enable)
                return;

            if (_loaded)
            {
                // LifeLoop calls Configure on every tick to support hot reload.
                // Rebuilding a 5000-entry index here would defeat the index itself,
                // so unchanged configurations are deliberately a constant-time no-op.
                if (!configChanged)
                    return;
                var evicted = TrimToCapacityLocked();
                _forgottenEntries += evicted;
                RebuildDerivedLocked();
                if (evicted > 0)
                    CompactLocked();
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

            var entry = NormalizeEntry(CreateEntry(report, _config.RewardScale));
            try
            {
                EnsureStoreDirectoryLocked();
                var duplicate = FindDuplicateLocked(entry);
                if (duplicate is not null)
                {
                    var index = _entries.IndexOf(duplicate);
                    var merged = MergeEntries(duplicate, entry);
                    _entries[index] = merged;
                    _duplicateMerges++;
                    _newEpisodesSinceConsolidation++;
                    RebuildDerivedLocked();
                    CompactLocked();
                    _lastStoredAt = merged.EndTs;
                    _lastError = null;
                    MaybeAutoConsolidateLocked();
                    return merged;
                }

                File.AppendAllText(_fullPath, JsonSerializer.Serialize(entry, _json) + Environment.NewLine);
                _entries.Add(entry);
                _linesOnDisk++;
                _newEpisodesSinceConsolidation++;
                _lastStoredAt = entry.EndTs;
                _lastError = null;

                var evicted = TrimToCapacityLocked();
                _forgottenEntries += evicted;
                RebuildDerivedLocked();
                PersistExperienceLocked();
                if (evicted > 0 || _linesOnDisk > _config.MaxEpisodes * 2)
                    CompactLocked();
                MaybeAutoConsolidateLocked();
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
            var result = BuildActionBiasLocked(cue);
            SetLastRecallLocked(result);
            return result;
        }
    }

    public MemoryExplanation Explain(MemoryCue cue)
    {
        lock (_gate)
        {
            EnsureConfiguredLocked();
            _recallRequests++;
            var result = BuildActionBiasLocked(cue);
            SetLastRecallLocked(result);
            var matches = RecallLocked(cue);
            var helpfulScore = result.HelpfulAction is not null && result.ActionBiases.TryGetValue(result.HelpfulAction, out var positive)
                ? positive
                : 0;
            var harmfulScore = result.HarmfulAction is not null && result.ActionBiases.TryGetValue(result.HarmfulAction, out var negative)
                ? negative
                : 0;
            var evidence = matches.Take(5).Select(match => new MemoryEvidence(
                match.Entry.EpisodeId,
                Math.Max(1, match.Entry.Occurrences),
                match.Similarity,
                match.Relevance,
                match.Entry.AvgReward,
                match.Entry.ScenarioPassed,
                BestAction(match.Entry.ActionRewardAverages, positiveOnly: true)
            )).ToList();

            return new MemoryExplanation(
                cue,
                result.IndexedCandidateCount,
                result.RecallCount,
                result.BestSimilarity,
                result.HelpfulAction,
                helpfulScore,
                result.HarmfulAction,
                harmfulScore,
                result.Confidence,
                result.ScenarioExperience,
                evidence
            );
        }
    }

    public MemoryConsolidationResult Consolidate()
    {
        lock (_gate)
        {
            EnsureConfiguredLocked();
            if (!_config.Enable)
                return new MemoryConsolidationResult(0, 0, 0, 0, 0, 0, 0);
            return ConsolidateLocked();
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

    public IReadOnlyList<ScenarioExperience> GetScenarioExperiences()
    {
        lock (_gate)
        {
            EnsureConfiguredLocked();
            return _experiences.Values
                .OrderByDescending(experience => experience.Confidence)
                .ThenBy(experience => experience.ScenarioName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public MemoryStatistics GetStatistics()
    {
        lock (_gate)
        {
            EnsureConfiguredLocked();
            return new MemoryStatistics(
                _entries.Count,
                _representedEpisodes,
                _experiences.Count,
                _experiences.Values.Count(experience => experience.IsMature),
                IndexedBucketCountLocked(),
                _lastCandidateCount,
                _duplicateMerges,
                _forgottenEntries,
                _consolidationRuns,
                _entries.Count == 0 ? 0 : _entries.Average(entry => entry.Salience),
                _entries.Count == 0 ? 0 : _entries.Average(entry => EffectiveStrength(entry)),
                _experiencePath
            );
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
            )
            {
                RepresentedEpisodes = _representedEpisodes,
                ScenarioExperiences = _experiences.Count,
                IndexedBuckets = IndexedBucketCountLocked(),
                LastCandidateCount = _lastCandidateCount,
                DuplicateMerges = _duplicateMerges,
                ForgottenEntries = _forgottenEntries,
                ConsolidationRuns = _consolidationRuns
            };
        }
    }

    private MemoryBiasResult BuildActionBiasLocked(MemoryCue cue)
    {
        if (!_config.Enable || _representedEpisodes < _config.MinEpisodesBeforeBias)
            return MemoryBiasResult.Empty;

        var matches = RecallLocked(cue);
        _experiences.TryGetValue(NormalizeKey(cue.ScenarioName), out var experience);
        if (matches.Count == 0 && (experience is null || !experience.IsMature))
            return MemoryBiasResult.Empty with { IndexedCandidateCount = _lastCandidateCount };

        var weightedOutcomes = new Dictionary<string, double>(StringComparer.Ordinal);
        var actionEvidence = new Dictionary<string, double>(StringComparer.Ordinal);
        var rewardScale = Math.Max(0.000001, _config.RewardScale);

        foreach (var match in matches)
        {
            var totalActions = match.Entry.ActionHistogram.Values.Where(count => count > 0).Sum();
            if (totalActions <= 0)
                continue;

            foreach (var (action, count) in match.Entry.ActionHistogram)
            {
                if (count <= 0)
                    continue;
                var share = count / (double)totalActions;
                var evidence = match.Relevance * share;
                var outcome = ActionOutcome(match.Entry, action, rewardScale);
                Add(weightedOutcomes, action, evidence * outcome);
                Add(actionEvidence, action, evidence);
            }
        }

        if (experience is { IsMature: true })
        {
            var scenarioEvidence = Math.Max(0.000001, experience.ActionEvidence.Values.Sum());
            foreach (var (action, outcome) in experience.ActionOutcomes)
            {
                var actionShare = experience.ActionEvidence.TryGetValue(action, out var rawEvidence)
                    ? rawEvidence / scenarioEvidence
                    : 0;
                var evidence = Math.Max(0.001, experience.Confidence * _config.ExperienceWeight * actionShare);
                Add(weightedOutcomes, action, outcome * evidence);
                Add(actionEvidence, action, evidence);
            }
        }

        var totalEvidence = Math.Max(0.000001, actionEvidence.Values.Sum());
        var biases = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (action, weightedOutcome) in weightedOutcomes)
        {
            var evidence = actionEvidence.TryGetValue(action, out var value) ? value : 0;
            if (evidence <= 0)
                continue;
            var outcome = weightedOutcome / evidence;
            var support = Math.Min(1.0, evidence * 2.0 / totalEvidence + (experience?.Confidence ?? 0) * 0.25);
            var bias = _config.MaxActionBias * outcome * support;
            biases[action] = Math.Clamp(bias, -_config.MaxActionBias, _config.MaxActionBias);
        }

        var helpful = biases.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
        var harmful = biases.Where(pair => pair.Value < 0).OrderBy(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
        var relevanceConfidence = matches.Count == 0 ? 0 : 1.0 - Math.Exp(-matches.Sum(match => match.Relevance));
        var confidence = Math.Clamp(Math.Max(relevanceConfidence, experience?.Confidence ?? 0), 0, 1);

        return new MemoryBiasResult(
            biases,
            matches.Count,
            matches.Count == 0 ? 0 : matches[0].Similarity,
            matches.Count == 0 ? null : matches[0].Entry.EpisodeId
        )
        {
            IndexedCandidateCount = _lastCandidateCount,
            HelpfulAction = helpful ?? experience?.HelpfulAction,
            HarmfulAction = harmful ?? experience?.HarmfulAction,
            Confidence = confidence,
            ScenarioExperience = experience
        };
    }

    private List<MemoryMatch> RecallLocked(MemoryCue cue)
    {
        var now = DateTimeOffset.UtcNow;
        var halfLifeDays = Math.Max(1.0, _config.RecencyHalfLifeDays);
        var candidates = CollectIndexedCandidatesLocked(cue);
        _lastCandidateCount = candidates.Count;

        return candidates
            .Select(entry =>
            {
                var similarity = Similarity(entry, cue);
                var ageDays = Math.Max(0, (now - entry.EndTs.ToUniversalTime()).TotalDays);
                var recency = Math.Pow(0.5, ageDays / halfLifeDays);
                var repetition = 1.0 + Math.Min(0.50, Math.Log2(Math.Max(1, entry.Occurrences)) * _config.RepeatBoost);
                var relevance = similarity * (0.35 + entry.Salience * 0.35 + EffectiveStrength(entry) * 0.30) * recency * repetition;
                return new MemoryMatch(entry, similarity, relevance);
            })
            .Where(match => match.Similarity >= _config.MinSimilarity)
            .OrderByDescending(match => match.Relevance)
            .Take(_config.RecallTopK)
            .ToList();
    }

    private List<EpisodicMemoryEntry> CollectIndexedCandidatesLocked(MemoryCue cue)
    {
        var maximum = _config.MaxIndexedCandidates;
        var result = new HashSet<EpisodicMemoryEntry>();
        var scenario = NormalizeKey(cue.ScenarioName);
        var mood = NormalizeKey(cue.Mood);
        var (pain, safety, arousal) = StateBuckets(cue.Pain, cue.Safety, cue.Arousal);

        foreach (var dp in NeighborOffsets)
        foreach (var ds in NeighborOffsets)
        foreach (var da in NeighborOffsets)
        {
            if (result.Count >= maximum)
                break;
            var p = pain + dp;
            var s = safety + ds;
            var a = arousal + da;
            if (!ValidBucket(p) || !ValidBucket(s) || !ValidBucket(a))
                continue;
            var bucket = StateBucket(p, s, a);
            AddFromIndex(_scenarioStateIndex, ScenarioStateKey(scenario, bucket), result, maximum);
        }

        foreach (var dp in NeighborOffsets)
        foreach (var ds in NeighborOffsets)
        foreach (var da in NeighborOffsets)
        {
            if (result.Count >= _config.RecallTopK)
                break;
            var p = pain + dp;
            var s = safety + ds;
            var a = arousal + da;
            if (!ValidBucket(p) || !ValidBucket(s) || !ValidBucket(a))
                continue;
            var bucket = StateBucket(p, s, a);
            AddFromIndex(_moodStateIndex, MoodStateKey(mood, bucket), result, maximum);
            AddFromIndex(_stateIndex, bucket, result, maximum);
        }

        if (result.Count < _config.RecallTopK)
            AddFromIndex(_scenarioIndex, scenario, result, maximum);

        return result
            .OrderByDescending(entry => Similarity(entry, cue) * (0.5 + EffectiveStrength(entry) * 0.5))
            .Take(maximum)
            .ToList();
    }

    private EpisodicMemoryEntry? FindDuplicateLocked(EpisodicMemoryEntry entry)
    {
        if (!_fingerprintIndex.TryGetValue(entry.Fingerprint, out var candidates))
            return null;
        return candidates.FirstOrDefault(candidate =>
            candidate.ScenarioPassed == entry.ScenarioPassed &&
            Similarity(candidate, ToCue(entry)) >= _config.DeduplicationSimilarity &&
            ActionSimilarity(candidate.ActionHistogram, entry.ActionHistogram) >= 0.85);
    }

    private MemoryConsolidationResult ConsolidateLocked()
    {
        var stopwatch = Stopwatch.StartNew();
        var before = _entries.Count;
        var representatives = new List<EpisodicMemoryEntry>();
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var merged = 0;

        foreach (var source in _entries.OrderBy(entry => entry.EndTs))
        {
            var entry = NormalizeEntry(source);
            var cluster = ClusterKey(entry);
            if (!groups.TryGetValue(cluster, out var indexes))
            {
                indexes = new List<int>();
                groups[cluster] = indexes;
            }

            var targetIndex = indexes.FirstOrDefault(index => CanConsolidate(representatives[index], entry), -1);
            if (targetIndex >= 0)
            {
                representatives[targetIndex] = MergeEntries(representatives[targetIndex], entry);
                merged++;
            }
            else
            {
                indexes.Add(representatives.Count);
                representatives.Add(entry);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var forgotten = representatives.RemoveAll(entry => ShouldForget(entry, now));
        _entries.Clear();
        _entries.AddRange(representatives.OrderBy(entry => entry.EndTs));
        forgotten += TrimToCapacityLocked();
        _forgottenEntries += forgotten;
        _duplicateMerges += merged;
        _consolidationRuns++;
        _newEpisodesSinceConsolidation = 0;
        RebuildDerivedLocked();
        CompactLocked();
        stopwatch.Stop();

        return new MemoryConsolidationResult(
            before,
            _entries.Count,
            _representedEpisodes,
            merged,
            forgotten,
            _experiences.Count,
            stopwatch.ElapsedMilliseconds
        );
    }

    private void MaybeAutoConsolidateLocked()
    {
        if (_newEpisodesSinceConsolidation >= _config.ConsolidateEveryEpisodes)
        {
            var result = ConsolidateLocked();
            _log($"ПАМЯТЬ: автоматическая консолидация записей={result.EntriesBefore}→{result.EntriesAfter} " +
                 $"объединено={result.MergedEntries} забыто={result.ForgottenEntries}");
        }
    }

    private bool CanConsolidate(EpisodicMemoryEntry left, EpisodicMemoryEntry right)
    {
        return left.ScenarioPassed == right.ScenarioPassed &&
               Similarity(left, ToCue(right)) >= _config.ConsolidationSimilarity &&
               ActionSimilarity(left.ActionHistogram, right.ActionHistogram) >= 0.75;
    }

    private bool ShouldForget(EpisodicMemoryEntry entry, DateTimeOffset now)
    {
        var ageDays = Math.Max(0, (now - entry.EndTs.ToUniversalTime()).TotalDays);
        if (ageDays < _config.ForgetAfterDays)
            return false;
        if (entry.Occurrences >= _config.ExperienceMinOccurrences * 2)
            return false;
        if (entry.Salience >= 0.80 || entry.LoopCount > 0 || IsCriticalEndReason(entry.EndReason))
            return false;

        var recency = Math.Pow(0.5, ageDays / Math.Max(1.0, _config.RecencyHalfLifeDays));
        return EffectiveStrength(entry) * recency < _config.ForgetThreshold;
    }

    private EpisodicMemoryEntry MergeEntries(EpisodicMemoryEntry left, EpisodicMemoryEntry right)
    {
        var leftOccurrences = Math.Max(1, left.Occurrences);
        var rightOccurrences = Math.Max(1, right.Occurrences);
        var occurrences = leftOccurrences + rightOccurrences;
        var newest = left.EndTs >= right.EndTs ? left : right;
        var histogram = MergeHistogram(left.ActionHistogram, right.ActionHistogram);
        var rewards = MergeActionRewards(left, right);
        var salience = Math.Clamp(
            Weighted(left.Salience, leftOccurrences, right.Salience, rightOccurrences) +
            Math.Min(0.20, Math.Log2(occurrences) * _config.RepeatBoost * 0.25),
            0,
            1);

        var merged = new EpisodicMemoryEntry(
            SchemaVersion,
            newest.EpisodeId,
            newest.ScenarioName,
            left.StartTs <= right.StartTs ? left.StartTs : right.StartTs,
            newest.EndTs,
            Math.Max(1, (int)Math.Round(Weighted(left.Steps, leftOccurrences, right.Steps, rightOccurrences))),
            newest.EndReason,
            Weighted(left.AvgReward, leftOccurrences, right.AvgReward, rightOccurrences),
            left.TotalReward + right.TotalReward,
            histogram,
            newest.DominantMood,
            Weighted(left.AvgPain, leftOccurrences, right.AvgPain, rightOccurrences),
            Weighted(left.AvgSafety, leftOccurrences, right.AvgSafety, rightOccurrences),
            Weighted(left.AvgArousal, leftOccurrences, right.AvgArousal, rightOccurrences),
            Math.Max(0, (int)Math.Round(Weighted(left.LoopCount, leftOccurrences, right.LoopCount, rightOccurrences))),
            newest.ScenarioPassed,
            salience
        )
        {
            Occurrences = occurrences,
            LastReinforcedAt = newest.EndTs,
            ActionRewardAverages = rewards
        };
        return NormalizeEntry(merged);
    }

    private void LoadLocked()
    {
        ClearDerivedLocked(clearEntries: true);
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
                        if (!ValidEntry(entry))
                        {
                            _invalidLines++;
                            continue;
                        }
                        _entries.Add(NormalizeEntry(entry!));
                    }
                    catch (JsonException)
                    {
                        _invalidLines++;
                    }
                }
            }

            var evicted = TrimToCapacityLocked();
            _forgottenEntries += evicted;
            RebuildDerivedLocked();
            _loaded = true;
            _lastStoredAt = _entries.Count > 0 ? _entries.Max(entry => entry.EndTs) : null;
            if (evicted > 0 || _linesOnDisk > _entries.Count * 2 + 10)
                CompactLocked();
            else
                PersistExperienceLocked();
            _log($"ПАМЯТЬ: загружено записей={_entries.Count} представлено эпизодов={_representedEpisodes} " +
                 $"сценариев={_experiences.Count} индекс={IndexedBucketCountLocked()} путь={_fullPath} повреждённых строк={_invalidLines}");
        }
        catch (Exception ex)
        {
            _loaded = true;
            _lastError = ex.Message;
            _log($"ПАМЯТЬ: не удалось загрузить хранилище: {ex.Message}");
        }
    }

    private void RebuildDerivedLocked()
    {
        _scenarioIndex.Clear();
        _stateIndex.Clear();
        _scenarioStateIndex.Clear();
        _moodStateIndex.Clear();
        _fingerprintIndex.Clear();

        for (var i = 0; i < _entries.Count; i++)
        {
            var normalized = NormalizeEntry(_entries[i]);
            _entries[i] = normalized;
            var scenario = NormalizeKey(normalized.ScenarioName);
            var mood = NormalizeKey(normalized.DominantMood);
            var bucket = StateBucket(normalized.AvgPain, normalized.AvgSafety, normalized.AvgArousal);
            AddToIndex(_scenarioIndex, scenario, normalized);
            AddToIndex(_stateIndex, bucket, normalized);
            AddToIndex(_scenarioStateIndex, ScenarioStateKey(scenario, bucket), normalized);
            AddToIndex(_moodStateIndex, MoodStateKey(mood, bucket), normalized);
            AddToIndex(_fingerprintIndex, normalized.Fingerprint, normalized);
        }

        SortIndexes(_scenarioIndex.Values);
        SortIndexes(_stateIndex.Values);
        SortIndexes(_scenarioStateIndex.Values);
        SortIndexes(_moodStateIndex.Values);
        _representedEpisodes = _entries.Sum(entry => Math.Max(1, entry.Occurrences));
        RebuildScenarioExperienceLocked();
    }

    private void RebuildScenarioExperienceLocked()
    {
        _experiences.Clear();
        foreach (var group in _entries.GroupBy(entry => NormalizeKey(entry.ScenarioName), StringComparer.OrdinalIgnoreCase))
        {
            var outcomes = new Dictionary<string, double>(StringComparer.Ordinal);
            var evidence = new Dictionary<string, double>(StringComparer.Ordinal);
            var episodeCount = 0;
            var successful = 0;
            var rewardSum = 0.0;
            var loopSum = 0.0;
            var lastUpdated = DateTimeOffset.MinValue;

            foreach (var entry in group)
            {
                var occurrences = Math.Max(1, entry.Occurrences);
                episodeCount += occurrences;
                if (entry.ScenarioPassed)
                    successful += occurrences;
                rewardSum += entry.AvgReward * occurrences;
                loopSum += entry.LoopCount * occurrences;
                if (entry.EndTs > lastUpdated)
                    lastUpdated = entry.EndTs;

                var totalActions = Math.Max(1, entry.ActionHistogram.Values.Where(value => value > 0).Sum());
                foreach (var (action, count) in entry.ActionHistogram)
                {
                    if (count <= 0)
                        continue;
                    var weight = Math.Max(0.001, occurrences * (count / (double)totalActions) * EffectiveStrength(entry));
                    Add(outcomes, action, ActionOutcome(entry, action, _config.RewardScale) * weight);
                    Add(evidence, action, weight);
                }
            }

            foreach (var action in outcomes.Keys.ToList())
                outcomes[action] = evidence[action] <= 0 ? 0 : Math.Clamp(outcomes[action] / evidence[action], -1, 1);

            var helpful = BestAction(outcomes, positiveOnly: true);
            var harmful = BestAction(outcomes, positiveOnly: false);
            var maturityTarget = Math.Max(2, _config.ExperienceMinOccurrences * 4);
            var confidence = Math.Clamp(Math.Log2(episodeCount + 1) / Math.Log2(maturityTarget + 1), 0, 1);
            var scenarioName = group.OrderByDescending(entry => entry.EndTs).First().ScenarioName;
            _experiences[group.Key] = new ScenarioExperience(
                scenarioName,
                episodeCount,
                successful,
                episodeCount == 0 ? 0 : successful / (double)episodeCount,
                episodeCount == 0 ? 0 : rewardSum / episodeCount,
                episodeCount == 0 ? 0 : loopSum / episodeCount,
                outcomes,
                evidence,
                helpful,
                harmful,
                confidence,
                episodeCount >= _config.ExperienceMinOccurrences,
                lastUpdated
            );
        }
    }

    private void CompactLocked()
    {
        var tempPath = _fullPath + ".tmp";
        try
        {
            EnsureStoreDirectoryLocked();
            File.WriteAllLines(tempPath, _entries.Select(entry => JsonSerializer.Serialize(entry, _json)));
            File.Move(tempPath, _fullPath, overwrite: true);
            _linesOnDisk = _entries.Count;
            PersistExperienceLocked();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _log($"ПАМЯТЬ: не удалось уплотнить хранилище: {ex.Message}");
            TryDelete(tempPath);
        }
    }

    private void PersistExperienceLocked()
    {
        var tempPath = _experiencePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_experiencePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_experiences.Values.OrderBy(value => value.ScenarioName), _prettyJson));
            File.Move(tempPath, _experiencePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _log($"ПАМЯТЬ: не удалось сохранить опыт по сценариям: {ex.Message}");
            TryDelete(tempPath);
        }
    }

    private int TrimToCapacityLocked()
    {
        var overflow = _entries.Count - _config.MaxEpisodes;
        if (overflow <= 0)
            return 0;
        var retained = _entries
            .OrderByDescending(entry => RetentionScore(entry, DateTimeOffset.UtcNow))
            .ThenByDescending(entry => entry.EndTs)
            .Take(_config.MaxEpisodes)
            .OrderBy(entry => entry.EndTs)
            .ToList();
        _entries.Clear();
        _entries.AddRange(retained);
        return overflow;
    }

    private void EnsureConfiguredLocked()
    {
        if (_configured)
            return;
        _configured = true;
        _config = Normalize(MemoryConfig.Default);
        _fullPath = Path.GetFullPath(_config.Path);
        _experiencePath = ResolveExperiencePath(_fullPath);
        if (_config.Enable)
            LoadLocked();
    }

    private void EnsureStoreDirectoryLocked()
    {
        var directory = Path.GetDirectoryName(_fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private void ClearDerivedLocked(bool clearEntries)
    {
        if (clearEntries)
        {
            _entries.Clear();
            _representedEpisodes = 0;
        }
        _scenarioIndex.Clear();
        _stateIndex.Clear();
        _scenarioStateIndex.Clear();
        _moodStateIndex.Clear();
        _fingerprintIndex.Clear();
        _experiences.Clear();
    }

    private void SetLastRecallLocked(MemoryBiasResult result)
    {
        _lastRecallCount = result.RecallCount;
        _lastBestSimilarity = result.BestSimilarity;
        _lastCandidateCount = result.IndexedCandidateCount;
    }

    private EpisodicMemoryEntry NormalizeEntry(EpisodicMemoryEntry entry)
    {
        var normalized = entry with
        {
            Occurrences = Math.Max(1, entry.Occurrences),
            Salience = Math.Clamp(entry.Salience, 0, 1),
            ActionHistogram = new Dictionary<string, int>(entry.ActionHistogram ?? new Dictionary<string, int>(), StringComparer.Ordinal),
            ActionRewardAverages = new Dictionary<string, double>(entry.ActionRewardAverages ?? new Dictionary<string, double>(), StringComparer.Ordinal),
            LastReinforcedAt = entry.LastReinforcedAt ?? entry.EndTs
        };
        normalized = normalized with
        {
            Strength = entry.Strength > 0 ? Math.Clamp(entry.Strength, 0, 1) : ComputeStrength(normalized),
            Fingerprint = string.IsNullOrWhiteSpace(entry.Fingerprint) ? Fingerprint(normalized) : entry.Fingerprint
        };
        return normalized;
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
        )
        {
            Occurrences = 1,
            LastReinforcedAt = report.EndTs,
            ActionRewardAverages = new Dictionary<string, double>(report.ActionRewardAverages, StringComparer.Ordinal)
        };
    }

    private static bool ValidEntry(EpisodicMemoryEntry? entry)
    {
        return entry is not null &&
               entry.SchemaVersion == SchemaVersion &&
               !string.IsNullOrWhiteSpace(entry.ScenarioName) &&
               !string.IsNullOrWhiteSpace(entry.DominantMood) &&
               !string.IsNullOrWhiteSpace(entry.EndReason) &&
               entry.ActionHistogram is not null;
    }

    private static double Similarity(EpisodicMemoryEntry entry, MemoryCue cue)
    {
        var stateDistance =
            Math.Abs(entry.AvgPain - LifeMath.Clamp01(cue.Pain)) +
            Math.Abs(entry.AvgSafety - LifeMath.Clamp01(cue.Safety)) +
            Math.Abs(entry.AvgArousal - LifeMath.Clamp01(cue.Arousal));
        var stateSimilarity = 1.0 - stateDistance / 3.0;
        var scenarioSimilarity = string.Equals(entry.ScenarioName, cue.ScenarioName, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
        var moodSimilarity = string.Equals(entry.DominantMood, cue.Mood, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
        return Math.Clamp(stateSimilarity * 0.55 + scenarioSimilarity * 0.30 + moodSimilarity * 0.15, 0, 1);
    }

    private static double ActionSimilarity(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right)
    {
        var keys = left.Keys.Union(right.Keys, StringComparer.Ordinal);
        var dot = 0.0;
        var leftNorm = 0.0;
        var rightNorm = 0.0;
        foreach (var key in keys)
        {
            var a = left.TryGetValue(key, out var leftValue) ? Math.Max(0, leftValue) : 0;
            var b = right.TryGetValue(key, out var rightValue) ? Math.Max(0, rightValue) : 0;
            dot += a * b;
            leftNorm += a * a;
            rightNorm += b * b;
        }
        if (leftNorm <= 0 || rightNorm <= 0)
            return 0;
        return dot / Math.Sqrt(leftNorm * rightNorm);
    }

    private static double ActionOutcome(EpisodicMemoryEntry entry, string action, double rewardScale)
    {
        var scale = Math.Max(0.000001, rewardScale);
        var localReward = entry.ActionRewardAverages.TryGetValue(action, out var reward) ? reward : entry.AvgReward;
        var localOutcome = Math.Tanh(localReward / scale);
        var globalOutcome = Math.Tanh(entry.AvgReward / scale);
        var outcome = localOutcome * 0.70 + globalOutcome * 0.30;
        if (!entry.ScenarioPassed)
            outcome = -Math.Max(0.25, Math.Abs(outcome));
        if (entry.LoopCount > 0)
            outcome -= Math.Min(0.40, entry.LoopCount * 0.10);
        return Math.Clamp(outcome, -1, 1);
    }

    private static double ComputeStrength(EpisodicMemoryEntry entry)
    {
        var repetition = Math.Min(0.30, Math.Log2(Math.Max(1, entry.Occurrences) + 1) * 0.08);
        var critical = entry.LoopCount > 0 || IsCriticalEndReason(entry.EndReason) ? 0.15 : 0;
        return Math.Clamp(0.15 + entry.Salience * 0.55 + repetition + critical, 0, 1);
    }

    private static double EffectiveStrength(EpisodicMemoryEntry entry)
    {
        return entry.Strength > 0 ? Math.Clamp(entry.Strength, 0, 1) : ComputeStrength(entry);
    }

    private double RetentionScore(EpisodicMemoryEntry entry, DateTimeOffset now)
    {
        var ageDays = Math.Max(0, (now - entry.EndTs.ToUniversalTime()).TotalDays);
        var recency = Math.Pow(0.5, ageDays / Math.Max(1.0, _config.RecencyHalfLifeDays));
        var repetition = 1.0 + Math.Min(0.50, Math.Log2(Math.Max(1, entry.Occurrences)) * _config.RepeatBoost);
        return EffectiveStrength(entry) * recency * repetition;
    }

    private static Dictionary<string, int> MergeHistogram(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right)
    {
        var merged = new Dictionary<string, int>(left, StringComparer.Ordinal);
        foreach (var (action, count) in right)
            merged[action] = merged.TryGetValue(action, out var current) ? current + count : count;
        return merged;
    }

    private static Dictionary<string, double> MergeActionRewards(EpisodicMemoryEntry left, EpisodicMemoryEntry right)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var action in left.ActionHistogram.Keys.Union(right.ActionHistogram.Keys, StringComparer.Ordinal))
        {
            var leftCount = left.ActionHistogram.TryGetValue(action, out var lc) ? Math.Max(0, lc) : 0;
            var rightCount = right.ActionHistogram.TryGetValue(action, out var rc) ? Math.Max(0, rc) : 0;
            var leftReward = left.ActionRewardAverages.TryGetValue(action, out var lr) ? lr : left.AvgReward;
            var rightReward = right.ActionRewardAverages.TryGetValue(action, out var rr) ? rr : right.AvgReward;
            var count = leftCount + rightCount;
            if (count > 0)
                result[action] = (leftReward * leftCount + rightReward * rightCount) / count;
        }
        return result;
    }

    private static double Weighted(double left, int leftWeight, double right, int rightWeight)
    {
        var total = Math.Max(1, leftWeight + rightWeight);
        return (left * leftWeight + right * rightWeight) / total;
    }

    private static string? BestAction(IReadOnlyDictionary<string, double> outcomes, bool positiveOnly)
    {
        var candidates = positiveOnly
            ? outcomes.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value)
            : outcomes.Where(pair => pair.Value < 0).OrderBy(pair => pair.Value);
        return candidates.Select(pair => pair.Key).FirstOrDefault();
    }

    private static MemoryCue ToCue(EpisodicMemoryEntry entry) => new(
        entry.ScenarioName,
        entry.DominantMood,
        entry.AvgPain,
        entry.AvgSafety,
        entry.AvgArousal
    );

    private static string Fingerprint(EpisodicMemoryEntry entry)
    {
        var topAction = entry.ActionHistogram.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key).FirstOrDefault() ?? "none";
        return $"{ClusterKey(entry)}|{topAction}";
    }

    private static string ClusterKey(EpisodicMemoryEntry entry)
    {
        var outcome = entry.ScenarioPassed ? "pass" : IsCriticalEndReason(entry.EndReason) ? "critical" : "fail";
        return $"{NormalizeKey(entry.ScenarioName)}|{NormalizeKey(entry.DominantMood)}|{outcome}|{StateBucket(entry.AvgPain, entry.AvgSafety, entry.AvgArousal)}";
    }

    private static string NormalizeKey(string? value) => (value ?? "").Trim().ToLowerInvariant();
    private static string ScenarioStateKey(string scenario, int bucket) => $"{scenario}|{bucket}";
    private static string MoodStateKey(string mood, int bucket) => $"{mood}|{bucket}";

    private static (int Pain, int Safety, int Arousal) StateBuckets(double pain, double safety, double arousal) =>
        (Quantize(pain), Quantize(safety), Quantize(arousal));

    private static int StateBucket(double pain, double safety, double arousal)
    {
        var (p, s, a) = StateBuckets(pain, safety, arousal);
        return StateBucket(p, s, a);
    }

    private static int StateBucket(int pain, int safety, int arousal) =>
        pain * StateBucketCount * StateBucketCount + safety * StateBucketCount + arousal;

    private static int Quantize(double value) => Math.Clamp((int)Math.Floor(LifeMath.Clamp01(value) * StateBucketCount), 0, StateBucketCount - 1);
    private static bool ValidBucket(int value) => value >= 0 && value < StateBucketCount;
    private static bool IsCriticalEndReason(string reason) =>
        string.Equals(reason, "panic", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "loop", StringComparison.OrdinalIgnoreCase);

    private static string ResolveExperiencePath(string episodicPath)
    {
        var directory = Path.GetDirectoryName(episodicPath) ?? ".";
        return Path.Combine(directory, "experience.json");
    }

    private int IndexedBucketCountLocked() =>
        _scenarioIndex.Count + _stateIndex.Count + _scenarioStateIndex.Count + _moodStateIndex.Count;

    private static void Add(Dictionary<string, double> target, string key, double value)
    {
        target[key] = target.TryGetValue(key, out var current) ? current + value : value;
    }

    private static void AddToIndex<TKey>(Dictionary<TKey, List<EpisodicMemoryEntry>> index, TKey key, EpisodicMemoryEntry entry)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var entries))
        {
            entries = new List<EpisodicMemoryEntry>();
            index[key] = entries;
        }
        entries.Add(entry);
    }

    private static void AddFromIndex<TKey>(
        Dictionary<TKey, List<EpisodicMemoryEntry>> index,
        TKey key,
        HashSet<EpisodicMemoryEntry> target,
        int maximum)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var entries))
            return;
        foreach (var entry in entries)
        {
            target.Add(entry);
            if (target.Count >= maximum)
                return;
        }
    }

    private static void SortIndexes(IEnumerable<List<EpisodicMemoryEntry>> indexes)
    {
        foreach (var entries in indexes)
        {
            entries.Sort((left, right) =>
            {
                var score = (right.Salience + EffectiveStrength(right)).CompareTo(left.Salience + EffectiveStrength(left));
                return score != 0 ? score : right.EndTs.CompareTo(left.EndTs);
            });
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The authoritative file is left untouched.
        }
    }

    private static MemoryConfig Normalize(MemoryConfig config)
    {
        var defaults = MemoryConfig.Default;
        return config with
        {
            Path = string.IsNullOrWhiteSpace(config.Path) ? defaults.Path : config.Path.Trim(),
            MaxEpisodes = Math.Clamp(config.MaxEpisodes, 10, 100_000),
            RecallTopK = Math.Clamp(config.RecallTopK, 1, 100),
            MinSimilarity = Math.Clamp(config.MinSimilarity, 0.0, 1.0),
            MaxActionBias = Math.Clamp(config.MaxActionBias, 0.0, 0.50),
            RecencyHalfLifeDays = Math.Clamp(config.RecencyHalfLifeDays, 1.0, 3650.0),
            MinEpisodesBeforeBias = Math.Clamp(config.MinEpisodesBeforeBias, 1, 10_000),
            MinEpisodeSteps = Math.Clamp(config.MinEpisodeSteps, 1, 1_000_000),
            RewardScale = Math.Clamp(config.RewardScale, 0.000001, 10.0),
            DeduplicationSimilarity = Math.Clamp(config.DeduplicationSimilarity <= 0 ? defaults.DeduplicationSimilarity : config.DeduplicationSimilarity, 0.50, 1.0),
            ConsolidationSimilarity = Math.Clamp(config.ConsolidationSimilarity <= 0 ? defaults.ConsolidationSimilarity : config.ConsolidationSimilarity, 0.50, 1.0),
            ConsolidateEveryEpisodes = Math.Clamp(config.ConsolidateEveryEpisodes <= 0 ? defaults.ConsolidateEveryEpisodes : config.ConsolidateEveryEpisodes, 1, 100_000),
            ForgetAfterDays = Math.Clamp(config.ForgetAfterDays <= 0 ? defaults.ForgetAfterDays : config.ForgetAfterDays, 1.0, 36_500.0),
            ForgetThreshold = Math.Clamp(config.ForgetThreshold <= 0 ? defaults.ForgetThreshold : config.ForgetThreshold, 0.001, 1.0),
            RepeatBoost = Math.Clamp(config.RepeatBoost <= 0 ? defaults.RepeatBoost : config.RepeatBoost, 0.001, 0.50),
            ExperienceMinOccurrences = Math.Clamp(config.ExperienceMinOccurrences <= 0 ? defaults.ExperienceMinOccurrences : config.ExperienceMinOccurrences, 1, 10_000),
            ExperienceWeight = Math.Clamp(config.ExperienceWeight <= 0 ? defaults.ExperienceWeight : config.ExperienceWeight, 0.01, 1.0),
            MaxIndexedCandidates = Math.Clamp(config.MaxIndexedCandidates <= 0 ? defaults.MaxIndexedCandidates : config.MaxIndexedCandidates, 16, 10_000)
        };
    }
}
