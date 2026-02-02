using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.BrainLife;

public sealed class MoodInertiaEngine
{
    private string _currentMood = "calm";
    private string _candidateMood = "calm";
    private int _candidateTicks;

    public (AffectDto affect, double moodInertia) Apply(AffectDto prev, AffectDto computed, double selfPreservation)
    {
        var alpha = 0.15 + 0.2 * LifeMath.Clamp01(computed.Arousal);
        var valence = Lerp(prev.Valence, computed.Valence, alpha);
        var arousal = Lerp(prev.Arousal, computed.Arousal, alpha);

        var mood = _currentMood;
        if (computed.Mood == _currentMood)
        {
            _candidateMood = _currentMood;
            _candidateTicks = 0;
        }
        else
        {
            if (selfPreservation > 0.85)
            {
                mood = computed.Mood;
                _currentMood = mood;
                _candidateTicks = 0;
                _candidateMood = mood;
            }
            else
            {
                if (_candidateMood != computed.Mood)
                {
                    _candidateMood = computed.Mood;
                    _candidateTicks = 1;
                }
                else
                {
                    _candidateTicks++;
                    if (_candidateTicks >= 3)
                    {
                        mood = computed.Mood;
                        _currentMood = mood;
                        _candidateTicks = 0;
                    }
                }
            }
        }

        var inertia = LifeMath.Clamp01(1.0 - alpha);
        var next = new AffectDto(mood, valence, arousal);
        return (next, inertia);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
