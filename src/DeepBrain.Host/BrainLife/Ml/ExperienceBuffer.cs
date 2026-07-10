namespace DeepBrain.Host.BrainLife.Ml;

public sealed class ExperienceBuffer
{
    private readonly Transition[] _buffer;
    private int _count;
    private int _index;

    public int Capacity => _buffer.Length;
    public int Count => _count;

    public ExperienceBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentException("Ёмкость должна быть больше нуля", nameof(capacity));
        _buffer = new Transition[capacity];
    }

    public void Clear()
    {
        _count = 0;
        _index = 0;
    }

    public void Add(Transition transition)
    {
        _buffer[_index] = transition;
        _index = (_index + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;
    }

    public IReadOnlyList<Transition> SampleBatch(int batchSize, Random rng)
    {
        if (_count == 0) return Array.Empty<Transition>();
        if (batchSize <= 0) return Array.Empty<Transition>();
        var n = Math.Min(batchSize, _count);
        var list = new List<Transition>(n);
        for (var i = 0; i < n; i++)
        {
            var idx = rng.Next(_count);
            list.Add(_buffer[idx]);
        }
        return list;
    }
}

public readonly record struct Transition(
    float[] State,
    int Action,
    float Reward,
    float[] NextState,
    bool Done,
    float[] ActionMask
);
