using System.Linq;

namespace DeepBrain.Host.BrainLife;

public sealed class PolicyEvaluator
{
    private readonly Queue<EpisodeReport> _training = new();
    private readonly Queue<EpisodeReport> _evaluation = new();
    private int _window = 20;
    private readonly ScenarioScorer _scorer;

    public PolicyEvaluator(ScenarioScorer scorer)
    {
        _scorer = scorer;
    }

    public void Configure(EvaluationConfig config)
    {
        _window = Math.Max(5, config.WindowEpisodes);
        Trim(_training);
        Trim(_evaluation);
    }

    public void Add(EpisodeReport report, bool isEvaluation)
    {
        var queue = isEvaluation ? _evaluation : _training;
        queue.Enqueue(report);
        Trim(queue);
    }

    public EvaluationSnapshot Snapshot(bool isEvaluation)
    {
        var queue = isEvaluation ? _evaluation : _training;
        return BuildSnapshot(queue);
    }

    private void Trim(Queue<EpisodeReport> queue)
    {
        while (queue.Count > _window)
            queue.Dequeue();
    }

    private EvaluationSnapshot BuildSnapshot(IEnumerable<EpisodeReport> reports)
    {
        var list = reports.ToList();
        if (list.Count == 0)
            return new EvaluationSnapshot(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var rewards = list.Select(r => r.TotalReward).ToList();
        rewards.Sort();
        var meanReward = rewards.Average();
        var medianReward = rewards[rewards.Count / 2];
        var avgEpisodeLength = list.Average(r => r.Steps);

        var loopEpisodes = list.Count(r => r.LoopCount > 0 || r.EndReason == "loop");
        var loopRate = loopEpisodes / (double)list.Count;

        var totalSteps = list.Sum(r => r.Steps);
        var actionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var moodCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var invalidCount = list.Sum(r => r.InvalidActionCount);
        var maskFallbackCount = list.Sum(r => r.MaskFallbackCount);

        foreach (var report in list)
        {
            foreach (var kv in report.ActionHistogram)
                actionCounts[kv.Key] = actionCounts.TryGetValue(kv.Key, out var count) ? count + kv.Value : kv.Value;

            foreach (var kv in report.MoodDistribution)
                moodCounts[kv.Key] = moodCounts.TryGetValue(kv.Key, out var count) ? count + kv.Value : kv.Value;
        }

        var actionDiversity = totalSteps > 0 ? actionCounts.Count / (double)totalSteps : 0.0;
        var calmRatio = totalSteps > 0 && moodCounts.TryGetValue("calm", out var calm) ? calm / (double)totalSteps : 0.0;
        var anxiousRatio = totalSteps > 0 && moodCounts.TryGetValue("anxious", out var anx) ? anx / (double)totalSteps : 0.0;
        var curiousRatio = totalSteps > 0 && moodCounts.TryGetValue("curious", out var cur) ? cur / (double)totalSteps : 0.0;
        var invalidRate = totalSteps > 0 ? invalidCount / (double)totalSteps : 0.0;
        var maskFallbackRate = totalSteps > 0 ? maskFallbackCount / (double)totalSteps : 0.0;

        var passed = list.Count(r => r.ScenarioScore?.Passed == true);
        var successRate = list.Count > 0 ? passed / (double)list.Count : 0.0;

        return new EvaluationSnapshot(
            meanReward,
            medianReward,
            successRate,
            avgEpisodeLength,
            loopRate,
            actionDiversity,
            calmRatio,
            anxiousRatio,
            curiousRatio,
            invalidRate,
            maskFallbackRate,
            list.Count
        );
    }
}

public sealed record EvaluationSnapshot(
    double MeanReward,
    double MedianReward,
    double SuccessRate,
    double AvgEpisodeLength,
    double LoopRate,
    double ActionDiversity,
    double CalmRatio,
    double AnxiousRatio,
    double CuriousRatio,
    double InvalidActionRate,
    double MaskFallbackRate,
    int EpisodeCount
);
