namespace DeepBrain.Host.BrainLife;

public sealed class EpisodeManager
{
    private int _episodeId;
    private int _episodeTick;
    private int _maxSteps;
    private double _loopStrengthThreshold;
    private int _loopHoldTicks;
    private int _loopHoldCount;
    private double _panicSafetyMin;
    private double _panicPainMin;
    private double _panicThreatMin;
    private string _lastResetReason = "none";
    private string? _requestedResetReason;

    public int EpisodeId => _episodeId;
    public int EpisodeTick => _episodeTick;
    public int EpisodeLengthTicks => _maxSteps;
    public string LastResetReason => _lastResetReason;

    public EpisodeManager(int maxSteps)
    {
        _maxSteps = Math.Max(1, maxSteps);
        _loopStrengthThreshold = 0.85;
        _loopHoldTicks = 6;
        _panicSafetyMin = 0.10;
        _panicPainMin = 0.95;
        _panicThreatMin = 0.90;
    }

    public void Configure(EpisodeConfig config)
    {
        _maxSteps = Math.Max(1, config.MaxSteps);
        _loopStrengthThreshold = Math.Clamp(config.LoopStrengthThreshold, 0.0, 1.0);
        _loopHoldTicks = Math.Max(1, config.LoopHoldTicks);
        _panicSafetyMin = Math.Clamp(config.PanicSafetyMin, 0.0, 1.0);
        _panicPainMin = Math.Clamp(config.PanicPainMin, 0.0, 1.0);
        _panicThreatMin = Math.Clamp(config.PanicThreatMin, 0.0, 1.0);
    }

    public void Configure(EpisodeConfig config, double lengthMultiplier)
    {
        var maxSteps = (int)Math.Round(config.MaxSteps * Math.Max(0.1, lengthMultiplier));
        _maxSteps = Math.Max(1, maxSteps);
        _loopStrengthThreshold = Math.Clamp(config.LoopStrengthThreshold, 0.0, 1.0);
        _loopHoldTicks = Math.Max(1, config.LoopHoldTicks);
        _panicSafetyMin = Math.Clamp(config.PanicSafetyMin, 0.0, 1.0);
        _panicPainMin = Math.Clamp(config.PanicPainMin, 0.0, 1.0);
        _panicThreatMin = Math.Clamp(config.PanicThreatMin, 0.0, 1.0);
    }

    public void RequestManualReset()
    {
        _requestedResetReason = "manual";
    }

    public void RequestScenarioComplete()
    {
        _requestedResetReason = "scenario_complete";
    }

    public void RequestReset(string reason)
    {
        _requestedResetReason = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim().ToLowerInvariant();
    }

    public void SetEpisodeId(int episodeId)
    {
        _episodeId = Math.Max(0, episodeId);
    }

    public bool Tick(double loopStrength, bool isLoop, bool isPanic, out string? reason)
    {
        reason = null;
        _episodeTick++;

        if (!string.IsNullOrWhiteSpace(_requestedResetReason))
        {
            reason = _requestedResetReason;
            _requestedResetReason = null;
            return true;
        }

        if (_episodeTick >= _maxSteps)
        {
            reason = "timeout";
            return true;
        }

        if (isPanic && (loopStrength <= _loopStrengthThreshold))
        {
            reason = "panic";
            return true;
        }

        if (isLoop && loopStrength >= _loopStrengthThreshold)
        {
            _loopHoldCount++;
            if (_loopHoldCount >= _loopHoldTicks)
            {
                reason = "loop";
                return true;
            }
        }
        else
        {
            _loopHoldCount = 0;
        }

        return false;
    }

    public void Reset(string reason)
    {
        _episodeId++;
        _episodeTick = 0;
        _loopHoldCount = 0;
        _lastResetReason = reason;
    }

    public bool IsPanic(double safety, double pain, double threat)
    {
        return safety <= _panicSafetyMin || pain >= _panicPainMin || threat >= _panicThreatMin;
    }
}
