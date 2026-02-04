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

    public long GetLastTick(string actionName)
    {
        return _lastTick.TryGetValue(actionName, out var last) ? last : -1;
    }

    public void Mark(string actionName, long tick)
    {
        _lastTick[actionName] = tick;
    }

    public void ResetAll()
    {
        _lastTick.Clear();
    }

    public void ResetExcept(params string[] keep)
    {
        if (keep.Length == 0)
        {
            _lastTick.Clear();
            return;
        }

        var set = new HashSet<string>(keep, StringComparer.Ordinal);
        var keys = _lastTick.Keys.ToArray();
        foreach (var key in keys)
        {
            if (!set.Contains(key))
                _lastTick.Remove(key);
        }
    }
}
