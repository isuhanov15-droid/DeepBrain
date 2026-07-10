namespace DeepBrain.Host.BrainLife;

public sealed class LoopDetector
{
    private readonly Queue<double> _recentRewards = new();
    private readonly Queue<string> _recentActions = new();
    private readonly Queue<string> _fingerprints = new();
    private readonly Queue<string> _stateKeys = new();
    private int _window = 32;
    private int _sameK = 8;
    private int _altK = 6;
    private const int RewardWindow = 15;
    private const double ProductiveRewardFloor = 0.0;

    private bool _inLoop;
    private long _lastLoopAnnounceTick = -1000;
    private string _lastFingerprint = "";
    private string _lastStateKey = "";
    private int _repeatStreak;
    private int _stateStreak;

    public string LastAction { get; private set; } = "none";
    public int SameActionStreak { get; private set; }
    public double AvgRewardShort { get; private set; }
    public int LoopObservedCount { get; private set; }
    public int LoopCount => LoopObservedCount;
    public double LoopPenalty { get; private set; }
    public string LoopType { get; private set; } = "none";
    public bool IsLoopDetected { get; private set; }
    public bool IsInLoop { get; private set; }
    public bool IsHeavyLoop { get; private set; }
    public double LoopStrength { get; private set; }
    public int Streak { get; private set; }
    public bool EnteredLoop { get; private set; }
    public bool LoopTypeChanged { get; private set; }
    public bool RecoveredFromLoop { get; private set; }
    public bool LoopEscalated { get; private set; }
    public bool IsProgressStalled { get; private set; }

    public void Configure(int window, int sameK, int altK)
    {
        _window = Math.Max(8, window);
        _sameK = Math.Max(4, sameK);
        _altK = Math.Max(3, altK);
    }

    public void Update(
        string actionName,
        string mood,
        double arousal,
        double energy,
        double fatigue,
        string dominantDrive,
        string phase,
        string attentionFocus,
        double reward,
        long tick)
    {
        EnteredLoop = false;
        LoopTypeChanged = false;
        RecoveredFromLoop = false;
        LoopEscalated = false;
        var previousType = LoopType;
        var previousStrength = LoopStrength;
        var previousInLoop = IsInLoop;

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

        var fingerprint = BuildFingerprint(actionName, mood, arousal, energy, fatigue, dominantDrive, phase, attentionFocus);
        var stateKey = BuildStateKey(mood, arousal, energy, fatigue, dominantDrive, phase, attentionFocus);

        _fingerprints.Enqueue(fingerprint);
        while (_fingerprints.Count > _window)
            _fingerprints.Dequeue();

        _stateKeys.Enqueue(stateKey);
        while (_stateKeys.Count > _window)
            _stateKeys.Dequeue();

        _repeatStreak = fingerprint == _lastFingerprint ? _repeatStreak + 1 : 1;
        _stateStreak = stateKey == _lastStateKey ? _stateStreak + 1 : 1;
        _lastFingerprint = fingerprint;
        _lastStateKey = stateKey;

        // A stable or repeated pattern is not automatically harmful. Calm states
        // and useful regulation actions may legitimately persist for many ticks.
        // Treat a pattern as a loop only after the short reward window shows that
        // it is no longer producing progress.
        var rewardIsUnproductive = AvgRewardShort <= ProductiveRewardFloor;
        var repeatHasEvidence = _recentRewards.Count >= _sameK;
        var altPatternLength = _altK * 2;
        var altHasEvidence = _recentRewards.Count >= altPatternLength;
        IsProgressStalled = rewardIsUnproductive
            && _recentRewards.Count >= Math.Min(_sameK, altPatternLength);
        var repeatLoop = _repeatStreak >= _sameK && repeatHasEvidence && rewardIsUnproductive;
        var altLoop = DetectAltLoop(_altK) && altHasEvidence && rewardIsUnproductive;
        var stuckThreshold = Math.Max(_sameK * 2, _sameK + 2);
        var stuckState = _stateStreak >= stuckThreshold
            && repeatHasEvidence
            && rewardIsUnproductive
            && !repeatLoop
            && !altLoop;

        if (repeatLoop)
        {
            LoopType = "repeat";
            Streak = _repeatStreak;
            LoopStrength = Math.Min(1.0, _repeatStreak / (double)_sameK);
        }
        else if (altLoop)
        {
            LoopType = "abab";
            Streak = _altK * 2;
            LoopStrength = 0.8;
        }
        else if (stuckState)
        {
            LoopType = "stuck_state";
            Streak = _stateStreak;
            LoopStrength = Math.Min(1.0, _stateStreak / (double)stuckThreshold);
        }
        else
        {
            LoopType = "none";
            Streak = 0;
            LoopStrength = 0;
        }

        IsLoopDetected = LoopType != "none";
        IsInLoop = IsLoopDetected;
        IsHeavyLoop = LoopStrength >= 0.85;
        LoopPenalty = IsInLoop ? LifeMath.Clamp01(LoopStrength) : 0.0;

        if (IsInLoop)
        {
            if (!_inLoop)
            {
                LoopObservedCount++;
                _inLoop = true;
            }
        }
        else
        {
            _inLoop = false;
        }

        EnteredLoop = !previousInLoop && IsInLoop;
        RecoveredFromLoop = previousInLoop && !IsInLoop;
        LoopTypeChanged = IsInLoop && previousInLoop && previousType != LoopType;
        LoopEscalated = IsInLoop && previousStrength < 0.85 && LoopStrength >= 0.85;
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
        _fingerprints.Clear();
        _stateKeys.Clear();
        SameActionStreak = 0;
        AvgRewardShort = 0;
        LoopObservedCount = 0;
        LoopPenalty = 0;
        LoopType = "none";
        IsLoopDetected = false;
        IsInLoop = false;
        IsHeavyLoop = false;
        LoopStrength = 0;
        Streak = 0;
        EnteredLoop = false;
        LoopTypeChanged = false;
        RecoveredFromLoop = false;
        LoopEscalated = false;
        IsProgressStalled = false;
        _repeatStreak = 0;
        _stateStreak = 0;
        _lastFingerprint = "";
        _lastStateKey = "";
        _inLoop = false;
    }

    private bool DetectAltLoop(int k2)
    {
        var needed = k2 * 2;
        if (_fingerprints.Count < needed) return false;
        var arr = _fingerprints.ToArray();
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

    private static string BuildFingerprint(
        string actionName,
        string mood,
        double arousal,
        double energy,
        double fatigue,
        string dominantDrive,
        string phase,
        string attentionFocus)
    {
        return string.Join("|",
            actionName,
            mood,
            Bucket(arousal),
            Bucket(energy),
            Bucket(fatigue),
            dominantDrive,
            phase,
            attentionFocus);
    }

    private static string BuildStateKey(
        string mood,
        double arousal,
        double energy,
        double fatigue,
        string dominantDrive,
        string phase,
        string attentionFocus)
    {
        return string.Join("|",
            mood,
            Bucket(arousal),
            Bucket(energy),
            Bucket(fatigue),
            dominantDrive,
            phase,
            attentionFocus);
    }

    private static string Bucket(double value)
    {
        var v = Math.Clamp(value, 0.0, 1.0);
        var b = (int)Math.Floor(v * 4.0);
        if (b < 0) b = 0;
        if (b > 4) b = 4;
        return b.ToString();
    }
}
