namespace DeepBrain.Host.BrainLife;

public sealed class MessageDedupeGuard
{
    private long _lastTick = -1000;
    private int _lastHash;

    public bool ShouldPublish(long tick, string message)
    {
        var hash = message.GetHashCode(StringComparison.Ordinal);
        if (hash == _lastHash && tick - _lastTick < 60)
            return false;

        _lastHash = hash;
        _lastTick = tick;
        return true;
    }
}
