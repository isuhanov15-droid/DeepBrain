namespace DeepBrain.Host.BrainLife;

public sealed class SelfTalkThrottle
{
    private long _cooldownTicks;
    private long _repeatCooldownTicks;
    private long _semanticCooldownTicks;
    private string? _lastText;
    private long _lastTick = -1000;
    private readonly Dictionary<string, long> _semanticLastTicks = new(StringComparer.Ordinal);

    public SelfTalkThrottle(long cooldownTicks = 120, long repeatCooldownTicks = 900, long semanticCooldownTicks = 600)
    {
        _cooldownTicks = cooldownTicks;
        _repeatCooldownTicks = repeatCooldownTicks;
        _semanticCooldownTicks = semanticCooldownTicks;
    }

    public void Configure(long cooldownTicks, long repeatCooldownTicks, long semanticCooldownTicks)
    {
        _cooldownTicks = Math.Max(1, cooldownTicks);
        _repeatCooldownTicks = Math.Max(_cooldownTicks, repeatCooldownTicks);
        _semanticCooldownTicks = Math.Max(_cooldownTicks, semanticCooldownTicks);
    }

    public bool ShouldSpeak(long tick, string text, string? semanticEvent = null)
    {
        var dt = tick - _lastTick;
        if (dt < _cooldownTicks)
            return false;

        if (string.Equals(text, _lastText, StringComparison.Ordinal) && dt < _repeatCooldownTicks)
            return false;

        if (!string.IsNullOrWhiteSpace(semanticEvent) &&
            _semanticLastTicks.TryGetValue(semanticEvent, out var lastSemanticTick) &&
            tick - lastSemanticTick < _semanticCooldownTicks)
        {
            return false;
        }

        _lastTick = tick;
        _lastText = text;
        if (!string.IsNullOrWhiteSpace(semanticEvent))
            _semanticLastTicks[semanticEvent] = tick;
        return true;
    }
}
