using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.ConsoleUi;

public static class ConsoleLineFormatter
{
    public static string FormatTickLine(LifeStateDto? state)
    {
        if (state is null)
            return "tick=n/a ep=n/a act=n/a reward=0.000 loop=none eps=0.000";

        var episodeId = state.Episode?.EpisodeId ?? 0;
        var action = string.IsNullOrWhiteSpace(state.LastDecision) ? "n/a" : state.LastDecision;
        var loopType = state.LoopInfo?.IsInLoop == true ? state.LoopInfo.Type : "none";
        var epsilon = state.Ml?.Epsilon ?? 0.0;
        return $"tick={state.Tick} ep={episodeId} act={action} reward={state.LastReward:0.000} loop={loopType} eps={epsilon:0.000}";
    }

    public static string FormatDecisionLine(LifeStateDto? state)
    {
        if (state?.Decision is null)
            return "decision act=n/a kind=n/a reason=n/a mask=ok";

        return $"decision act={state.Decision.ActionName} kind={state.Decision.ActionKind} reason={state.Decision.Reason} mask={state.Decision.MaskStatus}";
    }

    public static string FormatRewardLine(LifeStateDto? state)
    {
        if (state?.Reward is null)
            return "reward tot=+0.00 h=+0.00 x=+0.00 s=+0.00 lp=+0.00 ia=+0.00";

        var reward = state.Reward;
        return $"reward tot={reward.Total:+0.00;-0.00} h={reward.Homeostasis:+0.00;-0.00} x={reward.Explore:+0.00;-0.00} s={reward.Social:+0.00;-0.00} lp={reward.LoopPenalty:+0.00;-0.00} ia={reward.InvalidActionPenalty:+0.00;-0.00}";
    }

    public static string FormatEpisodeLine(LifeStateDto? state)
    {
        if (state?.Episode is null)
            return "episode=n/a";

        return $"episode={state.Episode.EpisodeId} tick={state.Episode.EpisodeTick}/{state.Episode.EpisodeLengthTicks} reason={state.Episode.ResetReason}";
    }

    public static string FormatScenarioLine(LifeStateDto? state)
    {
        if (state?.Scenario is null)
            return "scenario=n/a";

        return $"scenario={state.Scenario.Name} curriculum={state.Scenario.CurriculumMode} idx={state.Scenario.Index}";
    }

    public static string FitToWidth(string line, int width)
    {
        var safeWidth = Math.Max(8, width);
        if (line.Length <= safeWidth)
            return line;

        if (safeWidth <= 3)
            return line[..safeWidth];

        return line[..(safeWidth - 3)] + "...";
    }
}
