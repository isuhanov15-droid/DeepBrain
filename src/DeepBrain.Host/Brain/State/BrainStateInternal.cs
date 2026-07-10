namespace DeepBrain.Host.Brain.State;

public sealed class BrainStateInternal
{
    public const float BaselineStress = 0.2f;
    public const float BaselineEnergy = 0.7f;
    public const float BaselineFocus  = 0.7f;

    public float Stress { get; set; } = BaselineStress;
    public float Energy { get; set; } = BaselineEnergy;
    public float Focus  { get; set; } = BaselineFocus;

    public string Mood { get; set; } = "calm"; // Стабильный код: calm/anxious/tired/focused.

    public BrainStateInternal Clone() => new()
    {
        Stress = Stress,
        Energy = Energy,
        Focus  = Focus,
        Mood   = Mood
    };
}
