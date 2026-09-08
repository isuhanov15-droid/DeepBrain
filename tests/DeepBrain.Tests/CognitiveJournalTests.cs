using DeepBrain.Host.BrainLife;
using DeepBrain.Host.Cognition;
using Xunit;

namespace DeepBrain.Tests;

public sealed class CognitiveJournalTests
{
    [Fact]
    public void ValidatedReflectionsPersistAndRemainBounded()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "deepbrain-cortex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "inner-voice.jsonl");
            var config = new LlmConfig
            {
                Enable = true,
                PersistJournal = true,
                JournalPath = path,
                MaxJournalEntries = 10
            };
            var first = new CognitiveJournal(_ => { });
            first.Configure(config);
            for (var i = 1; i <= 12; i++)
                Assert.True(first.Append(Envelope(i)));

            Assert.Equal(10, first.GetRecent(100).Count);
            Assert.Equal(12L, first.GetLast()!.FrameId);

            var reloaded = new CognitiveJournal(_ => { });
            reloaded.Configure(config);

            Assert.Equal(10, reloaded.GetRecent(100).Count);
            Assert.Equal(12L, reloaded.GetLast()!.FrameId);
            Assert.Equal(0, reloaded.GetStatus().InvalidLines);
            Assert.True(reloaded.GetStatus().Loaded);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static CognitiveInsightEnvelope Envelope(long frameId) => new(
        CognitiveContract.Version,
        frameId,
        frameId * 10,
        frameId * 10,
        DateTimeOffset.UtcNow.AddSeconds(frameId),
        "ollama",
        "test-model",
        "test",
        TimeSpan.FromMilliseconds(5),
        new CognitiveInsight(
            "Состояние осмыслено",
            $"Внутренняя мысль {frameId}",
            "Что помогало раньше?",
            "low",
            0.9));
}
