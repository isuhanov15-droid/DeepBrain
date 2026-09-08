using DeepBrain.Host.BrainLife;
using Xunit;
namespace DeepBrain.Tests;
public sealed class MoodStatsWindowTests
{
    [Fact]
    public void EveryMoodContributesToTheDistribution()
    {
        var window = new MoodStatsWindow();
        foreach (var mood in new[] { "anxious", "calm", "curious", "neutral", "tender", "low", "frustrated", "new_mood" })
            window.Add(mood);
        var s = window.Snapshot(0.4);
        Assert.Equal(0.125, s.LowScore);
        Assert.Equal(0.125, s.FrustratedScore);
        Assert.Equal(0.125, s.OtherScore);
        Assert.Equal(1, s.AnxiousScore + s.CalmScore + s.CuriousScore + s.NeutralScore + s.TenderScore + s.LowScore + s.FrustratedScore + s.OtherScore);
        Assert.Equal(0.4, s.P95Pain);
    }

    [Fact]
    public void ResetDoesNotLeakPreviousEpisodeMood()
    {
        var window = new MoodStatsWindow();
        window.Add("low");
        window.Clear();
        window.Add("calm");
        Assert.Equal(0, window.Snapshot(0).LowScore);
        Assert.Equal(1, window.Snapshot(0).CalmScore);
    }
}
