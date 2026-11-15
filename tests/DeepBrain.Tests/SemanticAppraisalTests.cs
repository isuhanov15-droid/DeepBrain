using System.Reflection;
using System.Text.Json;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed partial class ConversationExperienceTests
{
    private static SemanticAppraisalDto Meaning(string kind="correction",string irony="likely",double confidence=.9,bool ambiguous=false)
        =>new(1,"qwen","qwen3.5:9b-q4_K_M",kind,irony,"assistant",confidence,ambiguous);
    [Fact]
    public void IronicThanksUsesMeaningAndNeverTheOldPositiveKeywordRule()
    {
        var c=New();var input=Input(1,"Ну спасибо, отлично помогла!") with { Semantic=Meaning() };
        c.Accept(input);c.Advance(.2);var s=c.Snapshot();
        Assert.Equal("correction",s.Last!.Kind);Assert.Contains("ирония",s.Last.Label);
        Assert.Equal(0,s.EmotionInfluence["joy"]);Assert.Equal(0,s.EmotionInfluence["affection"]);
        Assert.True(s.EmotionInfluence["curiosity"]>0);Assert.True(s.ValenceInfluence<0);
        var boundary=Capture(c);var resumed=New();Restore(resumed,boundary);resumed.Accept(input);resumed.Advance(0);
        Assert.Equal(1,resumed.Snapshot().Experiences);Assert.Equal(s.Last,resumed.Snapshot().Last);
        Assert.Throws<InvalidOperationException>(()=>resumed.Accept(input with { Semantic=Meaning("warmth","none") }));
    }
    [Theory]
    [InlineData(.4,false,"qwen")]
    [InlineData(.95,true,"qwen")]
    [InlineData(1,false,"unavailable")]
    public void UncertaintyAndOutagePreserveExperienceWithoutAnEmotionalAssumption(double confidence,bool ambiguous,string source)
    {
        var c=New();c.Accept(Input(1,"Спасибо") with { Semantic=Meaning("warmth","possible",confidence,ambiguous) with { Source=source } });
        c.Advance(1);var s=c.Snapshot();Assert.Equal(1,s.Experiences);Assert.Equal(0,s.EmotionalExchanges);
        Assert.Equal("uncertain",s.Last!.Kind);Assert.All(s.EmotionInfluence.Values,v=>Assert.Equal(0,v));
        Assert.Null(s.LastEmotional);
    }
    [Fact]
    public void LegacyJournalAndRuntimeHashRemainByteIdenticalWhenSemanticDataIsAbsent()
    {
        var c=New();c.Accept(Input(1,"Спасибо"));c.Advance(.2);var original=Capture(c).GetRawText();
        var path=Directory.GetFiles(_dir,"*.json").Single();var bytes=File.ReadAllBytes(path);
        Assert.DoesNotContain("semantic",original);Assert.DoesNotContain("semantic",File.ReadAllText(path));
        var restored=New();Restore(restored,Capture(c));Assert.Equal(original,Capture(restored).GetRawText());
        restored.Accept(Input(2,"Ну спасибо") with { Semantic=Meaning() });restored.Advance(.2);
        Assert.Equal(bytes,File.ReadAllBytes(path));Assert.Equal(2,restored.Snapshot().Experiences);
        Assert.Equal(2,JsonDocument.Parse(File.ReadAllBytes(Directory.GetFiles(_dir,"*.json").Order().Last())).RootElement.GetProperty("schemaVersion").GetInt32());
    }
    [Fact]
    public void SemanticMetadataAndRemainingInfluenceSurviveAnotherRestartExactly()
    {
        var c=New();c.Accept(Input(1,"Без тебя бы мучился") with { Semantic=Meaning("playful","possible",.8) });c.Advance(50);
        var captured=Capture(c);var expected=JsonSerializer.Serialize(c.Snapshot());
        var restored=New();Restore(restored,captured);
        Assert.Equal(expected,JsonSerializer.Serialize(restored.Snapshot()));
        Assert.All(restored.Snapshot().EmotionInfluence.Values,v=>Assert.InRange(v,0,.55));
    }
    [Fact]
    public void RejectsUnsupportedModelCommandsAndNonfiniteConfidence()
    {
        var c=New();foreach(var semantic in new[]{Meaning() with { Kind="delete_memory" },Meaning() with { Confidence=double.NaN },
            Meaning() with { Confidence=1.2 },Meaning() with { Model="cloud-model" },Meaning() with { Target="command" },
            Meaning() with { Irony="always" },Meaning() with { Version=2 }})
            Assert.Throws<ArgumentException>(()=>c.Accept(Input(1,"x") with { Semantic=semantic }));
    }
    [Fact]
    public void LargestPermittedUnicodeMessageCanBeReloaded()
    {
        var c=New();c.Accept(Input(1,string.Concat(Enumerable.Repeat("😀",4000))) with { Semantic=Meaning("neutral","none") });
        c.Advance(1);var restored=New();Restore(restored,Capture(c));Assert.Equal(1,restored.Snapshot().Experiences);
    }
}
