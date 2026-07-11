using System.Linq;

namespace DeepBrain.Host.BrainLife;

/// <summary>
/// Measures how evenly actions are distributed without making the score
/// depend on episode length. The Gini-Simpson index is zero for a single
/// repeated action and approaches one as usage spreads across actions.
/// </summary>
public static class ActionDiversity
{
    public static double Calculate(IReadOnlyDictionary<string, int> histogram)
    {
        var counts = histogram.Values
            .Where(count => count > 0)
            .Select(count => (double)count)
            .ToArray();

        var total = counts.Sum();
        if (total <= 0)
            return 0;

        var concentration = counts.Sum(count =>
        {
            var share = count / total;
            return share * share;
        });

        return Math.Clamp(1.0 - concentration, 0.0, 1.0);
    }
}
