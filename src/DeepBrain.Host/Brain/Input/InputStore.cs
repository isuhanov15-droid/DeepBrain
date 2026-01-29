using DeepBrain.Shared.Input;

namespace DeepBrain.Host.Brain.Input;

public sealed class InputStore
{
    private readonly object _lock = new();

    private BrainInputDto _current = new(
        Stress: 0.2f,
        Energy: 0.8f,
        Focus: 0.7f,
        Goal: "idle",
        Command: "none"
    );

    public BrainInputDto GetSnapshot()
    {
        lock (_lock) return _current;
    }

    public void Set(BrainInputDto input)
    {
        lock (_lock) _current = input;
    }

    // Частичное обновление (удобно для “я поменял только stress”)
    public void Patch(
        float? stress = null,
        float? energy = null,
        float? focus = null,
        string? goal = null,
        string? command = null)
    {
        lock (_lock)
        {
            _current = _current with
            {
                Stress = stress ?? _current.Stress,
                Energy = energy ?? _current.Energy,
                Focus  = focus  ?? _current.Focus,
                Goal   = goal   ?? _current.Goal,
                Command = command ?? _current.Command
            };
        }
    }
}
