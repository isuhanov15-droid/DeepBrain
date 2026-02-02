namespace DeepBrain.Host.BrainLife;

public sealed class ActionCooldowns
{
    private readonly Dictionary<string, long> _lastTick = new(StringComparer.Ordinal);

    public bool IsOnCooldown(string actionName, long tick, int cooldownTicks)
    {
        if (!_lastTick.TryGetValue(actionName, out var last))
            return false;
        return tick - last < cooldownTicks;
    }

    public void Mark(string actionName, long tick)
    {
        _lastTick[actionName] = tick;
    }
}
