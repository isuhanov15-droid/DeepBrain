using System.Text;
using System.Text.Json;
using DeepBrain.Host.BrainLife;

namespace DeepBrain.Host.Cognition;

public sealed record CognitiveJournalStatus(
    bool Enabled,
    bool Loaded,
    string Path,
    int EntryCount,
    int InvalidLines,
    long AppendCount,
    DateTimeOffset? LastStoredAt,
    string? LastError
);

/// <summary>
/// A bounded journal of validated LLM reflections. It is observational state:
/// entries cannot choose actions or mutate episodic memory.
/// </summary>
public sealed class CognitiveJournal
{
    private readonly object _gate = new();
    private readonly Action<string> _log;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private readonly List<CognitiveInsightEnvelope> _entries = new();

    private LlmConfig _config = LlmConfig.Default;
    private string _fullPath = Path.GetFullPath("memory/inner-voice.jsonl");
    private bool _configured;
    private bool _loaded;
    private int _invalidLines;
    private long _appendCount;
    private DateTimeOffset? _lastStoredAt;
    private string? _lastError;

    public CognitiveJournal(Action<string> log)
    {
        _log = log;
    }

    public void Configure(LlmConfig config)
    {
        var normalized = config.Normalize();
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(normalized.JournalPath);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _config = normalized with { PersistJournal = false };
                _configured = true;
                _lastError = ex.Message;
            }
            _log($"ВНУТРЕННИЙ ГОЛОС: некорректный путь журнала: {ex.Message}");
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
                _entries.Clear();
                _invalidLines = 0;
                _lastStoredAt = null;
                _lastError = null;
            }

            if (_loaded)
            {
                TrimLocked();
                return;
            }
            LoadLocked();
        }
    }

    public CognitiveInsightEnvelope? GetLast()
    {
        lock (_gate)
            return _entries.LastOrDefault();
    }

    public IReadOnlyList<CognitiveInsightEnvelope> GetRecent(int count)
    {
        lock (_gate)
        {
            return _entries
                .TakeLast(Math.Clamp(count, 1, 100))
                .Reverse()
                .ToList();
        }
    }

    public bool Append(CognitiveInsightEnvelope envelope)
    {
        lock (_gate)
        {
            if (!_configured)
                Configure(LlmConfig.Default);
            if (!_config.PersistJournal)
                return false;

            try
            {
                var directory = Path.GetDirectoryName(_fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var line = JsonSerializer.Serialize(envelope, _json) + Environment.NewLine;
                File.AppendAllText(_fullPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _entries.Add(envelope);
                _appendCount++;
                _lastStoredAt = envelope.CreatedAt;
                _lastError = null;
                if (_entries.Count > _config.MaxJournalEntries)
                    TrimLocked();
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _log($"ВНУТРЕННИЙ ГОЛОС: не удалось сохранить запись: {ex.Message}");
                return false;
            }
        }
    }

    public CognitiveJournalStatus GetStatus()
    {
        lock (_gate)
        {
            return new CognitiveJournalStatus(
                _configured && _config.PersistJournal,
                _loaded,
                _fullPath,
                _entries.Count,
                _invalidLines,
                _appendCount,
                _lastStoredAt,
                _lastError);
        }
    }

    private void LoadLocked()
    {
        _entries.Clear();
        _invalidLines = 0;
        if (!_config.PersistJournal || !File.Exists(_fullPath))
        {
            _loaded = true;
            return;
        }

        try
        {
            foreach (var line in File.ReadLines(_fullPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<CognitiveInsightEnvelope>(line, _json);
                    if (entry?.Insight is null)
                    {
                        _invalidLines++;
                        continue;
                    }
                    _entries.Add(entry);
                }
                catch (JsonException)
                {
                    _invalidLines++;
                }
            }
            TrimLocked();
            _lastStoredAt = _entries.LastOrDefault()?.CreatedAt;
            _loaded = true;
            _lastError = null;
            _log($"ВНУТРЕННИЙ ГОЛОС: журнал загружен, записей={_entries.Count} повреждённых строк={_invalidLines}");
        }
        catch (Exception ex)
        {
            _loaded = true;
            _lastError = ex.Message;
            _log($"ВНУТРЕННИЙ ГОЛОС: не удалось загрузить журнал: {ex.Message}");
        }
    }

    private void TrimLocked()
    {
        if (_entries.Count <= _config.MaxJournalEntries)
            return;
        _entries.RemoveRange(0, _entries.Count - _config.MaxJournalEntries);
        CompactLocked();
    }

    private void CompactLocked()
    {
        var temporaryPath = _fullPath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (var entry in _entries)
                    writer.WriteLine(JsonSerializer.Serialize(entry, _json));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _fullPath, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDelete(temporaryPath);
            _lastError = ex.Message;
            _log($"ВНУТРЕННИЙ ГОЛОС: не удалось уплотнить журнал: {ex.Message}");
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
        }
    }
}
