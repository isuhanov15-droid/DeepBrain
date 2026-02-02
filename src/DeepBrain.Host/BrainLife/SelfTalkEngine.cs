namespace DeepBrain.Host.BrainLife;

public sealed class SelfTalkEngine
{
    private readonly LadaStyleBank _style = new();

    public string? MaybeSpeak(SelfTalkContext ctx, string voiceMode, long tick)
    {
        if (ctx.EnteredSleep)
            return _style.Pick("rest", voiceMode, tick);
        if (ctx.WokeUp)
            return "Я проснулась. Начинаю новый цикл.";
        if (ctx.LoopPenalty > 0.5)
            return _style.Pick("loop", voiceMode, tick);
        if (ctx.Mood == "anxious" && ctx.SelfPreservation > 0.8)
            return _style.Pick("anxious", voiceMode, tick);
        if (ctx.AttentionFocus == "novelty" && ctx.HasCalmWindow)
            return _style.Pick("calm_window", voiceMode, tick);
        if (ctx.Reward > 0.25)
            return "Это сработало. Запомню.";
        return null;
    }
}

public sealed record SelfTalkContext(
    bool EnteredSleep,
    bool WokeUp,
    double LoopPenalty,
    string Mood,
    double SelfPreservation,
    string AttentionFocus,
    bool HasCalmWindow,
    double Reward
);
