using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Localization;

namespace DeepBrain.Host.ConsoleUi;

public static class ConsoleLineFormatter
{
    public static string FormatTickLine(LifeStateDto? state)
    {
        if (state is null)
            return "тик=нет эпизод=нет действие=нет награда=0.000 петля=нет ε=0.000";

        var episodeId = state.Episode?.EpisodeId ?? 0;
        var action = RussianDisplay.Token(state.LastDecision);
        var loopType = state.LoopInfo?.IsInLoop == true
            ? RussianDisplay.Token(state.LoopInfo.Type)
            : RussianDisplay.Token("none");
        var epsilon = state.Ml?.Epsilon ?? 0.0;
        return $"тик={state.Tick} эпизод={episodeId} действие={action} награда={state.LastReward:0.000} петля={loopType} ε={epsilon:0.000}";
    }

    public static string FormatDecisionLine(LifeStateDto? state)
    {
        if (state?.Decision is null)
            return "решение действие=нет тип=нет причина=нет маска=норма";

        return $"решение действие={RussianDisplay.Token(state.Decision.ActionName)} " +
               $"тип={RussianDisplay.Token(state.Decision.ActionKind)} " +
               $"причина={RussianDisplay.Token(state.Decision.Reason)} " +
               $"маска={RussianDisplay.Token(state.Decision.MaskStatus)}";
    }

    public static string FormatRewardLine(LifeStateDto? state)
    {
        if (state?.Reward is null)
            return "награда всего=+0.00 гомео=+0.00 исслед=+0.00 соц=+0.00 петля=+0.00 недоп=+0.00";

        var reward = state.Reward;
        return $"награда всего={reward.Total:+0.00;-0.00} " +
               $"гомео={reward.Homeostasis:+0.00;-0.00} " +
               $"исслед={reward.Explore:+0.00;-0.00} " +
               $"соц={reward.Social:+0.00;-0.00} " +
               $"петля={reward.LoopPenalty:+0.00;-0.00} " +
               $"недоп={reward.InvalidActionPenalty:+0.00;-0.00}";
    }

    public static string FormatEpisodeLine(LifeStateDto? state)
    {
        if (state?.Episode is null)
            return "эпизод=нет данных";

        return $"эпизод={state.Episode.EpisodeId} " +
               $"тик={state.Episode.EpisodeTick}/{state.Episode.EpisodeLengthTicks} " +
               $"причина={RussianDisplay.Token(state.Episode.ResetReason)}";
    }

    public static string FormatScenarioLine(LifeStateDto? state)
    {
        if (state?.Scenario is null)
            return "сценарий=нет данных";

        return $"сценарий={RussianDisplay.Token(state.Scenario.Name)} " +
               $"режим={RussianDisplay.Token(state.Scenario.CurriculumMode)} " +
               $"индекс={state.Scenario.Index}";
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
