namespace DeepBrain.Host.BrainLife;

public sealed class SelfTalkThrottle
{
    private long _cooldownTicks;
    private string? _lastText;
    private long _lastTick = -1000;

    public SelfTalkThrottle(long cooldownTicks = 40)
    {
        _cooldownTicks = cooldownTicks;
    }

    public void Configure(long cooldownTicks)
    {
        _cooldownTicks = Math.Max(1, cooldownTicks);
    }

    public bool ShouldSpeak(long tick, string text)
    {
        var dt = tick - _lastTick;
        if (dt < _cooldownTicks)
            return false;

        if (string.Equals(text, _lastText, StringComparison.Ordinal) && dt < _cooldownTicks * 3)
            return false;

        _lastTick = tick;
        _lastText = text;
        return true;
    }
}
