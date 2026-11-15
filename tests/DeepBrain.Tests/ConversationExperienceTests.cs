using System.Reflection;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using Xunit;

namespace DeepBrain.Tests;

public sealed class ConversationExperienceTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"deepbrain-social-"+Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now=DateTimeOffset.UtcNow;
    public void Dispose() { if(Directory.Exists(_dir)) Directory.Delete(_dir,true); }
    private ConversationExperience New()=>new(_dir,now:()=>_now);
    private ConversationInput Input(long seq,string text,bool historical=false)=>new(seq,seq.ToString("x32"),_now,text,historical);
    private static JsonElement Capture(ConversationExperience c)=>(JsonElement)typeof(ConversationExperience)
        .GetMethod("CaptureRuntimeState",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(c,null)!;
    private static void Restore(ConversationExperience c,JsonElement state)=>typeof(ConversationExperience)
        .GetMethod("RestoreRuntimeState",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(c,new object[]{state});

    [Theory]
    [InlineData("Лада, спасибо! Ты мне помогла.","warmth","affection")]
    [InlineData("У меня получилось! Я рад!","joy","joy")]
    [InlineData("Мне грустно и тяжело сегодня.","support","sadness")]
    [InlineData("Ты ошиблась, ответ неправильный.","correction","curiosity")]
    [InlineData("Почему небо голубое?","interest","curiosity")]
    public void UserWordsAffectBoundedEmotions(string text,string kind,string emotion)
    {
        var c=New();c.Accept(Input(1,text));c.Advance(.2);var s=c.Snapshot();
        Assert.Equal(kind,s.Last!.Kind);Assert.True(s.EmotionInfluence[emotion]>.2);
        Assert.Equal(1,s.Experiences);Assert.Equal(1,s.EmotionalExchanges);
        Assert.All(s.EmotionInfluence.Values,x=>Assert.InRange(x,0,.55));
    }
    [Theory]
    [InlineData("Представь, что мне грустно")]
    [InlineData("Переведи: спасибо")]
    [InlineData("Цитата: я рад")]
    [InlineData("Лада, запомни: мне грустно в этой песне")]
    public void QuotesAndImaginedFeelingsAreNotCurrentFeelings(string message)
    {
        var c=New();c.Accept(Input(1,message));c.Advance(1);
        Assert.Equal("neutral",c.Snapshot().Last!.Kind);
        Assert.Equal(0,c.Snapshot().EmotionInfluence["sadness"]);
    }
    [Fact]
    public void NegatedAndQuotedJoyDoesNotTriggerJoy()
    {
        var c=New();c.Accept(Input(1,"Я не рад. «Спасибо» — это просто цитата."));c.Advance(1);
        Assert.Equal(0,c.Snapshot().EmotionInfluence["joy"]);
    }
    [Fact]
    public void DuplicateTimeoutRetryAndRestartNeverDoubleApply()
    {
        var input=Input(1,"Спасибо!");var first=New();first.Accept(input);first.Accept(input);
        first.Advance(.2);var state=Capture(first);var before=JsonSerializer.Serialize(first.Snapshot());
        var second=New();Restore(second,state);second.Accept(input);
        Assert.Equal(before,JsonSerializer.Serialize(second.Snapshot()));
        second.Advance(0);Assert.Equal(1,second.Snapshot().Experiences);
        Assert.Equal(first.Snapshot().EmotionInfluence,second.Snapshot().EmotionInfluence);
        Assert.Throws<InvalidOperationException>(()=>second.Accept(input with { Message="Другой текст" }));
    }
    [Fact]
    public void ObservationsAfterCheckpointReplayAndPendingInputSurvives()
    {
        var c=New();c.Accept(Input(1,"Привет"));c.Advance(1);var boundary=Capture(c);
        c.Accept(Input(2,"Мне грустно"));c.Advance(1);var wanted=JsonSerializer.Serialize(c.Snapshot());
        var resumed=New();Restore(resumed,boundary);resumed.Advance(1);
        Assert.Equal(wanted,JsonSerializer.Serialize(resumed.Snapshot()));
    }
    [Fact]
    public void HistoricalAndStaleMessagesBecomeExperienceWithoutPresentMood()
    {
        var c=New();c.Accept(Input(1,"Спасибо!",true));c.Accept(Input(2,"Мне грустно") with { At=_now.AddHours(-1) });
        c.Advance(1);var s=c.Snapshot();Assert.Equal(2,s.Experiences);Assert.Equal(0,s.EmotionalExchanges);
        Assert.All(s.EmotionInfluence.Values,x=>Assert.Equal(0,x));Assert.Equal(0,s.ValenceInfluence);
    }
    [Fact]
    public void EmotionDecaysButExperienceRemainsAndRepetitionIsCapped()
    {
        var c=New();for(var i=1;i<=100;i++){ c.Accept(Input(i,"Спасибо"));c.Advance(.2); }
        Assert.All(c.Snapshot().EmotionInfluence.Values,x=>Assert.InRange(x,0,.55));
        for(var i=0;i<30;i++)c.Advance(60);
        Assert.All(c.Snapshot().EmotionInfluence.Values,x=>Assert.InRange(x,0,.001));
        Assert.Equal(100,c.Snapshot().Experiences);Assert.Equal(100,Directory.GetFiles(_dir,"*.json").Length);
    }
    [Fact]
    public void WarmConversationChangesAffectButCannotOverrideDanger()
    {
        var c=New();c.Accept(Input(1,"Спасибо"));c.Advance(.2);
        var a=new AffectDto("calm",.1,.2);var body=new HomeostasisDto(.7,.1,.2,0,1);
        Assert.Equal("tender",c.Blend(a,body).Mood);Assert.True(c.Blend(a,body).Valence>a.Valence);
        Assert.Equal("anxious",c.Blend(a with { Mood="anxious" },body with { Pain=.7 }).Mood);
    }
    [Fact]
    public void MissingOrModifiedAcknowledgedExperienceStopsRestore()
    {
        var c=New();c.Accept(Input(1,"Привет"));c.Advance(1);var boundary=Capture(c);
        File.Delete(Directory.GetFiles(_dir,"*.json")[0]);
        Assert.Throws<TargetInvocationException>(()=>Restore(New(),boundary));
    }
    [Fact]
    public void RejectsInvalidIdentityFutureAndReorderedInputs()
    {
        var c=New();Assert.Throws<ArgumentException>(()=>c.Accept(Input(1,"x") with { RequestId="../oops" }));
        Assert.Throws<ArgumentException>(()=>c.Accept(Input(1,"x") with { At=_now.AddHours(1) }));
        c.Accept(Input(2,"x"));Assert.Throws<InvalidOperationException>(()=>c.Accept(Input(1,"y")));
        Assert.Throws<InvalidOperationException>(()=>c.Accept(Input(3,"x") with { RequestId=Input(2,"x").RequestId }));
    }
}
