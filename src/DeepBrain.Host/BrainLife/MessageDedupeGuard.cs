namespace DeepBrain.Host.BrainLife;

public sealed partial class MessageDedupeGuard
{
    private long _lastTick = -1000;
    private string? _lastMessage;

    public bool ShouldPublish(long tick, string message)
    {
        if (string.Equals(message, _lastMessage, StringComparison.Ordinal) && tick - _lastTick < 60)
            return false;

        _lastMessage = message;
        _lastTick = tick;
        return true;
    }
}
