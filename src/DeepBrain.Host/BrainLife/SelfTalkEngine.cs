namespace DeepBrain.Host.BrainLife;

public sealed class SelfTalkEngine
{
    private readonly LadaStyleBank _style = new();

    public string? MaybeSpeak(SelfTalkContext ctx, string voiceMode, long tick)
    {
        if (ctx.LoopEndedByEpisode)
            return _style.Pick("loop", voiceMode, tick);

        if ((ctx.EnteredLoop || ctx.LoopTypeChanged) && ctx.LoopStrength >= ctx.LoopMinStrength)
            return _style.Pick("loop", voiceMode, tick);

        if (ctx.LoopRecovered)
            return "Петля отпустила. Держу новый курс.";

        if (ctx.CalmWindowEntered)
            return _style.Pick("calm_window", voiceMode, tick);

        return null;
    }
}

public sealed record SelfTalkContext(
    bool EnteredLoop,
    bool LoopTypeChanged,
    bool LoopRecovered,
    bool CalmWindowEntered,
    bool LoopEndedByEpisode,
    string LoopType,
    double LoopStrength,
    double LoopMinStrength
);
