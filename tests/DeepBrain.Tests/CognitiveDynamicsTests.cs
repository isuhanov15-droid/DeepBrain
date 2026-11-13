using System.Reflection;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class CognitiveDynamicsTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"deepbrain-cognition-"+Guid.NewGuid().ToString("N"));
    public CognitiveDynamicsTests()=>Directory.CreateDirectory(_dir);
    public void Dispose()=>Directory.Delete(_dir,true);
    private CognitiveDynamicsConfig Config=>new(){Enable=true,Path=Path.Combine(_dir,"cognition.json"),EmotionResponse=.5};
    private CognitiveDynamics Make(CognitiveDynamicsConfig? cfg=null){var c=new CognitiveDynamics();c.Configure(cfg??Config);return c;}
    private static CognitiveObservation O(double threat=0,double energy=.7,double novelty=.5,double loop=0)=>
        new("calm_baseline",new HomeostasisDto(energy,.2,.1,0,.9),novelty,.4,threat,.5,loop,false);
    private static readonly WorldEventDto[] NoEvents=Array.Empty<WorldEventDto>();
    private static ActionDto A(string name="rest_short",double strength=.5)=>new("internal",name,strength,null);
    private static ExecutiveStateDto E(params GoalOutcomeDto[] outcomes)=>new(true,0,null,0,0,0,outcomes,null,null,null,0,"idle",false);
    private static GoalOutcomeDto Outcome(string id,string status)=>new(id,"restore_energy","Восстановить силы",status,"test","Измеренный результат",10,0,1,DateTimeOffset.UtcNow);

    [Fact] public void DisabledLayerDoesNotCreateStorageOrChangeActionScores()
    { var c=Make(Config with{Enable=false});c.Begin(O(),NoEvents,null);var scores=new Dictionary<string,double>{{"rest_short",.3}};c.ApplyBias(scores,null);c.Flush();Assert.Equal(.3,scores["rest_short"]);Assert.False(File.Exists(Config.Path)); }

    [Fact] public void EmotionsCanCoexistAndThreatRaisesFear()
    { var c=Make();c.Begin(O(threat:.9),NoEvents,null);var e=c.Snapshot().Emotions;Assert.Equal(10,e.Count);Assert.True(e.Single(x=>x.Id=="fear").Intensity>.4);Assert.True(e.Single(x=>x.Id=="curiosity").Intensity>0);Assert.All(e,x=>Assert.InRange(x.Intensity,0,1)); }

    [Fact] public void OldGoalHistoryDoesNotReplayButNewSuccessAndFailureHaveDifferentEffects()
    {
        var c=Make();var old=Outcome("old","failed");c.Begin(O(energy:.2),NoEvents,E(old));
        Assert.Equal(0,c.Snapshot().GoalOutcomes);
        var success=Outcome("new","succeeded");c.Begin(O(),NoEvents,E(success,old));
        Assert.True(c.Snapshot().Emotions.Single(x=>x.Id=="joy").Intensity>0);
        var failed=Outcome("failed","failed");c.Begin(O(energy:.2),NoEvents,E(failed,success,old));
        Assert.True(c.Snapshot().Emotions.Single(x=>x.Id=="sadness").Intensity>0);
        c.Begin(O(),NoEvents,E(failed,success,old));Assert.Equal(2,c.Snapshot().GoalOutcomes);
    }

    [Fact] public void WorkingMemoryIsBoundedExpiresAndChangesDecisionRelevance()
    {
        var c=Make(Config with{WorkingMemoryCapacity=4,WorkingMemoryTtlTicks=20});
        c.Begin(O(novelty:0),new[]{new WorldEventDto(0,"novelty_opportunity",.9,"новый сигнал",0,.9,false)},null);
        Assert.Single(c.Snapshot().WorkingMemory);Assert.True(c.Snapshot().Emotions.Single(x=>x.Id=="curiosity").Intensity>0);
        for(int i=0;i<10;i++)c.Begin(O(),new[]{new WorldEventDto(i,"cue"+i,.8,"сигнал",0,.8,false)},null);
        Assert.Equal(4,c.Snapshot().WorkingMemory.Count);
        for(int i=0;i<21;i++)c.Begin(O(),NoEvents,null);Assert.Empty(c.Snapshot().WorkingMemory);
    }

    [Fact] public void ContextSwitchRemovesWorkingCuesButKeepsLearnedEffects()
    {var c=Make();c.Begin(O(),new[]{new WorldEventDto(0,"social_ping",1,"контакт",0,1,false)},null);c.Observe(A(),O(),O(energy:.72),false,0,null);c.Begin(O() with{Scenario="fatigue_day"},NoEvents,null);Assert.Empty(c.Snapshot().WorkingMemory);Assert.Equal(1,c.Snapshot().LearnedModels);}

    [Fact] public void ExpectationsLearnBeforeComparisonAndNormalizeActionStrength()
    {
        var c=Make();
        for(int i=0;i<10;i++){c.Begin(O(),NoEvents,null);c.ApplyBias(new Dictionary<string,double>{{"rest_short",.2}},"restore_energy");c.Observe(A(),O(),O(energy:.72),false,0,null);}
        var learned=c.Snapshot().Expectation!;Assert.Equal("checked",learned.Status);Assert.True(learned.Confidence>0);Assert.InRange(learned.Error!.Value,0,1e-8);
        c.Begin(O(),NoEvents,null);c.ApplyBias(new Dictionary<string,double>{{"rest_short",.2}},"restore_energy");c.Observe(A(strength:1),O(),O(energy:.74),false,0,null);
        Assert.InRange(c.Snapshot().Expectation!.Error!.Value,0,1e-8);
        c.Begin(O(),NoEvents,null);c.Observe(A(),O(),O(energy:.3),false,0,null);
        Assert.True(c.Snapshot().Expectation!.Error>.05);
        c.Begin(O(),NoEvents,null);Assert.True(c.Snapshot().Emotions.Single(x=>x.Id=="surprise").Intensity>0);
    }

    [Fact] public void InvalidActionsDoNotTeachTheModelOrInventConfidence()
    {var c=Make();c.Begin(O(),NoEvents,null);c.Observe(A(),O(),O(energy:.9),true,0,null);Assert.Equal(0,c.Snapshot().Observations);Assert.Null(c.Snapshot().Expectation);c.Observe(A(),O(),O(energy:.72),false,0,null);Assert.Equal(0,c.Snapshot().Expectation!.Confidence);Assert.Null(c.Snapshot().Expectation!.Error);}

    [Fact] public void BiasCannotOpenMaskedActionsOrEncourageExplorationInDanger()
    {
        var c=Make(Config with{MaxActionBias=100});c.Begin(O(threat:.9) with{Body=new HomeostasisDto(.2,.9,.8,.7,.1)},NoEvents,null);
        var scores=new Dictionary<string,double>{{"breathe_slow",.2},{"explore_signal",.4},{"emit_message",.1},{"rest_short",double.NegativeInfinity}};
        c.ApplyBias(scores,"regulate");Assert.True(double.IsNegativeInfinity(scores["rest_short"]));Assert.Equal(.4,scores["explore_signal"]);Assert.Equal(.1,scores["emit_message"]);Assert.InRange(scores["breathe_slow"],.2,.35);
    }

    [Fact] public void SleepFreezesClockMemoryAndLearningAndPersistsProgress()
    {var c=Make();c.Begin(O(),NoEvents,null);c.Observe(A(),O(),O(energy:.72),false,0,null);var clock=c.Snapshot().Clock;for(int i=0;i<100;i++){c.Begin(O() with{Sleeping=true},NoEvents,null);c.Observe(A(),O(),O(energy:.8),false,0,null);}Assert.Equal(clock,c.Snapshot().Clock);Assert.Equal(1,c.Snapshot().Observations);Assert.True(File.Exists(Config.Path));}

    [Fact] public void RestartRestoresModelsMemoryEmotionsWithoutReplayingOldResults()
    {
        var c=Make();c.Begin(O(),NoEvents,E());var result=Outcome("done","succeeded");
        c.Begin(O(),NoEvents,E(result));c.Observe(A(),O(),O(energy:.72),false,0,E(result));c.Flush();var before=c.Snapshot();
        var loaded=Make();var after=loaded.Snapshot();Assert.Equal(before.Clock,after.Clock);Assert.Equal(before.Observations,after.Observations);Assert.Equal(before.LearnedModels,after.LearnedModels);Assert.Equal(before.WorkingMemory,after.WorkingMemory);Assert.Equal(before.Emotions.Select(x=>x.Intensity),after.Emotions.Select(x=>x.Intensity));
        loaded.Begin(O(),NoEvents,E(result));Assert.Equal(before.GoalOutcomes,loaded.Snapshot().GoalOutcomes);
    }

    [Theory] [InlineData("{}")] [InlineData("{\"schemaVersion\":9}")]
    public void IncompleteOrUnknownStateIsNotOverwritten(string input)
    {File.WriteAllText(Config.Path,input);Assert.ThrowsAny<Exception>(()=>Make());Assert.Equal(input,File.ReadAllText(Config.Path));}

    [Fact] public void FailedSaveRetainsObservationsAndRetriesWithoutReset()
    {
        var blocker=Path.Combine(_dir,"blocked");var cfg=Config with{Path=Path.Combine(blocker,"cognition.json")};
        var c=Make(cfg);c.Begin(O(),NoEvents,null);c.Observe(A(),O(),O(energy:.72),false,0,null);
        File.WriteAllText(blocker,"occupied");c.Flush();Assert.NotNull(c.Snapshot().LastError);
        Assert.Equal(1,c.Snapshot().Observations);Assert.False(File.Exists(cfg.Path));
        File.Delete(blocker);c.Flush();Assert.Null(c.Snapshot().LastError);
        Assert.Equal(1,Make(cfg).Snapshot().Observations);
    }

    [Fact] public void RealLifeLoopUsesAndSavesCognitiveLayerWithExistingPlans()
    {
        var config=BrainConfig.Default with{CognitiveDynamics=Config,Planning=new PlanningConfig{Enable=true,Path=Path.Combine(_dir,"planning.json")},
            Ml=BrainConfig.Default.Ml with{Enable=false,Backend="off"},Memory=MemoryConfig.Default with{Enable=false},
            Habits=HabitConfig.Default with{Enable=false,ImportPath=""},Llm=LlmConfig.Default with{Enable=false,PersistJournal=false},
            Evaluation=BrainConfig.Default.Evaluation with{SaveReports=false}};
        var file=Path.Combine(_dir,"config.json");File.WriteAllText(file,JsonSerializer.Serialize(config));var states=new List<LifeStateDto>();
        var loop=new LifeLoop(new WorldSim(1337),new HomeostasisEngine(),new InstinctEngine(),new EmotionEngine(),new ActionSelector(),new Actuator(),new RewardEngine(),new LearningEngine(),new BrainConfigLoader(file,_=>{}),(s,_)=>states.Add(s),(_,_)=>{},(_,_)=>{},_=>{});
        var tick=typeof(LifeLoop).GetMethod("TickOnce",BindingFlags.NonPublic|BindingFlags.Instance)!;
        for(int i=0;i<1400;i++)tick.Invoke(loop,new object[]{.2,CancellationToken.None});loop.FlushPersistentState();
        Assert.Contains(states,s=>s.Cognition!.LearnedModels>1);Assert.Contains(states,s=>s.Cognition!.Expectation?.Status=="checked");
        Assert.Contains(states,s=>s.Cognition!.ActionBiases.Values.Any(x=>x!=0));Assert.Contains(states,s=>s.Cognition!.GoalOutcomes>0);
        Assert.Contains(states,s=>s.Cognition!.Sleeping);Assert.Contains(states,s=>s.Executive!.Succeeded>0);
        Assert.True(Make().Snapshot().Observations>0);
    }
}
