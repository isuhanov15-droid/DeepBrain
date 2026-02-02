namespace DeepBrain.Host.BrainLife;

public sealed class LoopDetector
{
    private readonly Queue<double> _recentRewards = new();
    private const int Window = 15;

    private bool _inLoop;

    public string LastAction { get; private set; } = "none";
    public int SameActionStreak { get; private set; }
    public double AvgRewardShort { get; private set; }
    public int LoopCount { get; private set; }
    public double LoopPenalty { get; private set; }

    public void Update(string actionName, double reward)
    {
        if (actionName == LastAction)
            SameActionStreak++;
        else
            SameActionStreak = 1;

        LastAction = actionName;

        _recentRewards.Enqueue(reward);
        while (_recentRewards.Count > Window)
            _recentRewards.Dequeue();

        AvgRewardShort = _recentRewards.Count == 0 ? 0 : _recentRewards.Average();

        var loopDetected = (SameActionStreak >= 5 && AvgRewardShort < 0) || SameActionStreak >= 8;

        if (loopDetected)
        {
            LoopPenalty = LifeMath.Clamp01(LoopPenalty + 0.15);
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
}
