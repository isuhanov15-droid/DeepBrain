namespace DeepBrain.Host.BrainLife;

public sealed record CognitiveDynamicsConfig
{
    public bool Enable { get; init; }
    public string Path { get; init; } = "memory/cognition.json";
    public int WorkingMemoryCapacity { get; init; } = 12;
    public int WorkingMemoryTtlTicks { get; init; } = 300;
    public int SaveEveryTicks { get; init; } = 50;
    public double MaxActionBias { get; init; } = 0.08;
    public double EmotionResponse { get; init; } = 0.12;
    public static CognitiveDynamicsConfig Default => new();
    public CognitiveDynamicsConfig Normalize() => this with
    {
        Path = string.IsNullOrWhiteSpace(Path) ? "memory/cognition.json" : Path.Trim(),
        WorkingMemoryCapacity = Math.Clamp(WorkingMemoryCapacity, 4, 32),
        WorkingMemoryTtlTicks = Math.Clamp(WorkingMemoryTtlTicks, 20, 1800),
        SaveEveryTicks = Math.Clamp(SaveEveryTicks, 1, 1000),
        MaxActionBias = Math.Clamp(double.IsFinite(MaxActionBias) ? MaxActionBias : 0.08, 0, 0.15),
        EmotionResponse = Math.Clamp(double.IsFinite(EmotionResponse) ? EmotionResponse : 0.12, 0.01, 0.5)
    };
}
