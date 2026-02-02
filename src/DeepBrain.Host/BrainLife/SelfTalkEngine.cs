namespace DeepBrain.Host.BrainLife;

public sealed class SelfTalkEngine
{
    public string? MaybeSpeak(SelfTalkContext ctx)
    {
        if (ctx.EnteredSleep)
            return "Пора восстановиться. Перехожу в сон.";
        if (ctx.WokeUp)
            return "Я проснулся. Начинаю новый цикл.";
        if (ctx.LoopPenalty > 0.5)
            return "Я зациклился. Меняю подход.";
        if (ctx.Mood == "anxious" && ctx.SelfPreservation > 0.8)
            return "Я в тревоге. Сужаю фокус и дышу.";
        if (ctx.AttentionFocus == "novelty" && ctx.HasCalmWindow)
            return "Можно исследовать: безопасное окно.";
        if (ctx.Reward > 0.2)
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
