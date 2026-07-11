namespace DeepBrain.Host.BrainLife;

public sealed class ScenarioScorer
{
    public ScenarioScore Score(string scenarioName, EpisodeReport report)
    {
        var name = scenarioName.Trim().ToLowerInvariant();
        var steps = Math.Max(1, report.Steps);
        var loopRate = report.LoopCount / (double)steps;
        var anxiousRatio = report.MoodDistribution.TryGetValue("anxious", out var anx) ? anx / (double)steps : 0.0;
        var calmRatio = report.MoodDistribution.TryGetValue("calm", out var calm) ? calm / (double)steps : 0.0;
        var curiousRatio = report.MoodDistribution.TryGetValue("curious", out var cur) ? cur / (double)steps : 0.0;
        var actionDiversity = ActionDiversity.Calculate(report.ActionHistogram);

        return name switch
        {
            "calm_baseline" => loopRate < 0.10 && anxiousRatio < 0.20
                ? new ScenarioScore(true, "loopRate<0.10 anxious<0.20")
                : new ScenarioScore(false, $"loopRate={loopRate:0.00} anxious={anxiousRatio:0.00}"),
            "novelty_walk" => report.RewardBreakdownAvg.Explore > 0.01 && actionDiversity > 0.20
                ? new ScenarioScore(true, "explore>0.01 diversity>0.20")
                : new ScenarioScore(false, $"explore={report.RewardBreakdownAvg.Explore:0.000} div={actionDiversity:0.00}"),
            "threat_pulses" => report.AvgSafety > 0.35 && report.EndReason != "panic"
                ? new ScenarioScore(true, "safety>0.35 no panic")
                : new ScenarioScore(false, $"safety={report.AvgSafety:0.00} reason={report.EndReason}"),
            "social_pull" => report.SocialSignalsSent > 0
                ? new ScenarioScore(true, "socialSignals>0")
                : new ScenarioScore(false, "no social signals"),
            "fatigue_day" => report.ActionHistogram.TryGetValue("rest_short", out var restCount) && restCount > 0 && report.AvgPain < 0.65
                ? new ScenarioScore(true, "rest>0 pain<0.65")
                : new ScenarioScore(false, $"rest={restCount} pain={report.AvgPain:0.00}"),
            "mixed_adaptive" => actionDiversity > 0.20 && report.AvgReward > 0
                ? new ScenarioScore(true, "diversity>0.20 avgReward>0")
                : new ScenarioScore(false, $"div={actionDiversity:0.00} avgR={report.AvgReward:0.000}"),
            _ => new ScenarioScore(true, "no criteria")
        };
    }

}
