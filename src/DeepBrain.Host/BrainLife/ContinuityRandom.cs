namespace DeepBrain.Host.BrainLife;

// Version 1 uses xorshift64*. Its entire state is one nonzero word, so restoring
// a long-running simulation is constant time. Used only with continuity enabled.
internal sealed class ContinuityRandom : Random
{
    public ulong State { get; private set; }
    public ContinuityRandom(int seed) : this(unchecked((ulong)(uint)seed) + 0x9E3779B97F4A7C15UL) { }
    public ContinuityRandom(ulong state)
    {
        if (state == 0) throw new InvalidDataException("Invalid checkpoint random state");
        State = state;
    }
    protected override double Sample()
    {
        var x = State;
        x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
        State = x;
        return (unchecked(x * 2685821657736338717UL) >> 11) * (1.0 / (1UL << 53));
    }
    public override double NextDouble() => Sample();
    public override int Next() => Next(int.MaxValue);
    public override int Next(int maxValue) => Next(0, maxValue);
    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
        return (int)(minValue + (long)(Sample() * ((long)maxValue - minValue)));
    }
}
