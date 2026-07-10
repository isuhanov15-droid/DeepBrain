using System.Security.Cryptography;
using System.Text.Json;

namespace DeepBrain.Host.BrainLife;

public sealed class BrainConfigLoader
{
    private readonly string _path;
    private readonly Action<string> _log;
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private BrainConfig _current = BrainConfig.Default;
    private string _version = "default";
    private DateTime _lastWrite;
    private DateTime _lastCheck = DateTime.MinValue;

    public BrainConfigLoader(string path, Action<string> log)
    {
        _path = path;
        _log = log;
        TryReload(force: true);
    }

    public (BrainConfig Config, string Version) GetCurrent()
    {
        if ((DateTime.UtcNow - _lastCheck).TotalSeconds >= 2)
        {
            TryReload(force: false);
            _lastCheck = DateTime.UtcNow;
        }

        return (_current, _version);
    }

    public void ReloadNow()
    {
        TryReload(force: true);
        _version = $"{_version}-r{DateTime.UtcNow:HHmmss}";
    }

    private void TryReload(bool force)
    {
        try
        {
            if (!File.Exists(_path))
                return;

            var lastWrite = File.GetLastWriteTimeUtc(_path);
            if (!force && lastWrite <= _lastWrite)
                return;

            var json = File.ReadAllBytes(_path);
            var cfg = JsonSerializer.Deserialize<BrainConfig>(json, _options);
            if (cfg is null)
            {
                _log("Предупреждение: разбор brainconfig вернул null; сохранена последняя корректная конфигурация");
                return;
            }

            _current = cfg;
            _lastWrite = lastWrite;
            _version = ComputeHash(json);
            _log($"Конфигурация brainconfig загружена, версия={_version}");
        }
        catch (Exception ex)
        {
            _log($"Предупреждение: не удалось перезагрузить brainconfig: {ex.Message}");
        }
    }

    private static string ComputeHash(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}
