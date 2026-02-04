namespace DeepBrain.Host.BrainLife;

public sealed class LoopDetector
{
    private readonly Queue<double> _recentRewards = new();
    private readonly Queue<string> _recentActions = new();
    private int _window = 32;
    private int _sameK = 8;
    private int _altK = 6;
    private const int RewardWindow = 15;

    private bool _inLoop;
    private long _lastLoopAnnounceTick = -1000;

    public string LastAction { get; private set; } = "none";
    public int SameActionStreak { get; private set; }
    public double AvgRewardShort { get; private set; }
    public int LoopCount { get; private set; }
    public double LoopPenalty { get; private set; }
    public string LoopType { get; private set; } = "none";
    public bool IsLoopDetected { get; private set; }
    public bool IsHeavyLoop { get; private set; }

    public void Configure(int window, int sameK, int altK)
    {
        _window = Math.Max(8, window);
        _sameK = Math.Max(4, sameK);
        _altK = Math.Max(3, altK);
    }

    public void Update(string actionName, double reward, long tick)
    {
        if (actionName == LastAction)
            SameActionStreak++;
        else
            SameActionStreak = 1;

        LastAction = actionName;
        _recentActions.Enqueue(actionName);
        while (_recentActions.Count > _window)
            _recentActions.Dequeue();

        _recentRewards.Enqueue(reward);
        while (_recentRewards.Count > RewardWindow)
            _recentRewards.Dequeue();

        AvgRewardShort = _recentRewards.Count == 0 ? 0 : _recentRewards.Average();

        var sameLoop = SameActionStreak >= _sameK;
        var altLoop = DetectAltLoop(_altK);
        IsLoopDetected = sameLoop || altLoop;
        LoopType = sameLoop ? "same" : altLoop ? "alt" : "none";
        IsHeavyLoop = SameActionStreak >= (_sameK + 4) || (altLoop && AvgRewardShort < 0);

        if (IsLoopDetected)
        {
            LoopPenalty = LifeMath.Clamp01(LoopPenalty + 0.12);
            if (!_inLoop)
            {
                LoopCount++;
                _inLoop = true;
            }
        }
        else
        {
            LoopPenalty = LifeMath.Clamp01(LoopPenalty - 0.05);
            _inLoop = false;
        }
    }

    public bool ShouldAnnounceLoop(long tick)
    {
        if (!IsLoopDetected) return false;
        if (tick - _lastLoopAnnounceTick < 40) return false;
        _lastLoopAnnounceTick = tick;
        return true;
    }

    public void ResetShortTerm()
    {
        _recentRewards.Clear();
        _recentActions.Clear();
        SameActionStreak = 0;
        AvgRewardShort = 0;
        LoopPenalty = 0;
        LoopType = "none";
        IsLoopDetected = false;
        IsHeavyLoop = false;
        _inLoop = false;
    }

    private bool DetectAltLoop(int k2)
    {
        var needed = k2 * 2;
        if (_recentActions.Count < needed) return false;
        var arr = _recentActions.ToArray();
        var start = arr.Length - needed;
        var a = arr[start];
        var b = arr[start + 1];
        if (a == b) return false;
        for (var i = 0; i < needed; i++)
        {
            var expected = (i % 2 == 0) ? a : b;
            if (arr[start + i] != expected)
                return false;
        }
        return true;
    }
}
