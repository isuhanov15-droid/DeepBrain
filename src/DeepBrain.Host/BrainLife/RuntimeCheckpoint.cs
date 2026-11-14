using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed record ContinuityConfig(bool Enable = false, string Path = "memory/runtime-state.json", int SaveEveryTicks = 500);

internal sealed class RuntimeCheckpoint
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = false, MaxDepth = 64 };
    [JsonRequired] public int SchemaVersion { get; set; } = 2;
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public DateTimeOffset SavedAt { get; set; }
    [JsonRequired] public string ConfigHash { get; set; } = "";
    [JsonRequired] public string StateHash { get; set; } = "";
    [JsonRequired] public Dictionary<string, JsonElement> Components { get; set; } = new();
    // null means the file did not exist at this boundary (valid for unused stores).
    [JsonRequired] public Dictionary<string, string?> Files { get; set; } = new();

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static string HashState(Dictionary<string, JsonElement> state) => Hash(JsonSerializer.SerializeToUtf8Bytes(state, Json));
    internal static string ConfigurationHash(BrainConfig config) => Hash(JsonSerializer.SerializeToUtf8Bytes(config, Json));
    internal static string? FileHash(string path)
    {
        if (!File.Exists(path)) return null;
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
    internal static T Deserialize<T>(JsonElement element)
    {
        ValidateJson(element);
        return element.Deserialize<T>(Json) ?? throw new InvalidDataException("Empty runtime checkpoint component");
    }
    private static void ValidateJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject())
            {
                if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate checkpoint property: " + p.Name);
                ValidateJson(p.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            if (element.GetArrayLength() > 20_000) throw new InvalidDataException("Checkpoint collection is too large");
            foreach (var item in element.EnumerateArray()) ValidateJson(item);
        }
        else if (element.ValueKind == JsonValueKind.Number && (!element.TryGetDouble(out var v) || !double.IsFinite(v)))
            throw new InvalidDataException("Non-finite checkpoint value");
    }

    internal static RuntimeCheckpoint? LoadAndValidate(BrainConfig config)
    {
        var settings = config.Continuity;
        if (settings?.Enable != true) return null;
        if (settings.SaveEveryTicks < 1 || string.IsNullOrWhiteSpace(settings.Path))
            throw new InvalidDataException("Invalid continuity configuration");
        if (!File.Exists(settings.Path))
        {
            if (File.Exists(settings.Path + ".required"))
                throw new InvalidDataException("Required runtime checkpoint is missing. Existing experience has not been changed.");
            return null; // First upgrade, existing memory is retained.
        }
        if (new FileInfo(settings.Path).Length > 64 * 1024 * 1024)
            throw new InvalidDataException("Runtime checkpoint exceeds 64 MB");
        using var document = JsonDocument.Parse(File.ReadAllBytes(settings.Path));
        var snapshot = Deserialize<RuntimeCheckpoint>(document.RootElement);
        // The only automatic configuration migration is v1.9 -> v1.10's new
        // foresight section. Every existing setting, file and runtime component
        // still has to match the old boundary exactly.
        var configMatches = snapshot.ConfigHash == ConfigurationHash(config) ||
            snapshot.SchemaVersion == 1 && snapshot.ConfigHash == ConfigurationHash(config with { Foresight = null });
        if (snapshot.SchemaVersion is not (1 or 2) || !Guid.TryParseExact(snapshot.Id, "N", out _) ||
            snapshot.Components is null || snapshot.Files is null ||
            snapshot.SchemaVersion == 1 && snapshot.Components.ContainsKey("foresight") ||
            !configMatches || snapshot.StateHash != HashState(snapshot.Components))
            throw new InvalidDataException("Runtime checkpoint is incompatible or damaged. Existing experience has not been changed.");
        var current = CaptureFiles(config);
        if (!current.Keys.Order(StringComparer.Ordinal).SequenceEqual(snapshot.Files.Keys.Order(StringComparer.Ordinal)) ||
            current.Any(x => snapshot.Files[x.Key] != x.Value))
            throw new InvalidDataException("Runtime checkpoint and persistent experience belong to different boundaries. Startup stopped without overwriting memory.");
        return snapshot;
    }

    internal static Dictionary<string, string?> CaptureFiles(BrainConfig config)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        void Add(string path) => paths.Add(System.IO.Path.GetFullPath(path));
        var memory = config.Memory ?? MemoryConfig.Default;
        var habits = config.Habits ?? HabitConfig.Default;
        var planning = config.Planning ?? PlanningConfig.Default;
        var cognition = config.CognitiveDynamics ?? CognitiveDynamicsConfig.Default;
        if (memory.Enable) { Add(memory.Path); Add(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(memory.Path) ?? ".", "experience.json")); }
        if (habits.Enable) Add(habits.Path);
        if (planning.Enable) Add(planning.Path);
        if (cognition.Enable) Add(cognition.Path);
        if (config.Ml.Enable || config.UseMlAdvisor)
        {
            Add(config.Ml.CheckpointPath);
            using var outer = JsonDocument.Parse(File.ReadAllBytes(config.Ml.CheckpointPath));
            var backendPath = outer.RootElement.GetProperty("WeightsPath").GetString()!;
            Add(backendPath);
            if (string.Equals(config.Ml.Backend, "remote", StringComparison.OrdinalIgnoreCase))
            {
                using var remote = JsonDocument.Parse(File.ReadAllBytes(backendPath));
                foreach (var key in new[] { "weightsPath", "targetWeightsPath", "replayPath" })
                {
                    var path = remote.RootElement.GetProperty(key).GetString()!;
                    if (!File.Exists(path)) throw new InvalidDataException("Missing ML checkpoint asset: " + key);
                    Add(path);
                }
            }
        }
        return paths.Order(StringComparer.Ordinal).ToDictionary(x => x, FileHash, StringComparer.Ordinal);
    }

    internal static void AtomicWrite<T>(string path, T data)
    {
        path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, data, Json); stream.Flush(true); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
