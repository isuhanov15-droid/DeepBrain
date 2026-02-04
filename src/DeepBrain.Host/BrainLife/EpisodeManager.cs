namespace DeepBrain.Host.BrainLife;

public sealed class EpisodeManager
{
    private int _episodeId;
    private int _episodeTick;
    private int _episodeLengthTicks;
    private string _lastResetReason = "none";
    private bool _manualResetRequested;

    public int EpisodeId => _episodeId;
    public int EpisodeTick => _episodeTick;
    public int EpisodeLengthTicks => _episodeLengthTicks;
    public string LastResetReason => _lastResetReason;

    public EpisodeManager(int episodeLengthTicks)
    {
        _episodeLengthTicks = Math.Max(1, episodeLengthTicks);
    }

    public void Configure(int episodeLengthTicks)
    {
        _episodeLengthTicks = Math.Max(1, episodeLengthTicks);
    }

    public void RequestManualReset()
    {
        _manualResetRequested = true;
    }

    public void SetEpisodeId(int episodeId)
    {
        _episodeId = Math.Max(0, episodeId);
    }

    public bool Tick(out string? reason)
    {
        reason = null;
        _episodeTick++;

        if (_manualResetRequested)
        {
            _manualResetRequested = false;
            reason = "manual";
            return true;
        }

        if (_episodeTick >= _episodeLengthTicks)
        {
            reason = "length";
            return true;
        }

        return false;
    }

    public void Reset(string reason)
    {
        _episodeId++;
        _episodeTick = 0;
        _lastResetReason = reason;
    }
}
