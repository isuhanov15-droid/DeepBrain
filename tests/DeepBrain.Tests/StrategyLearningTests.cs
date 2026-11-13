using System.Text.Json;
using System.Text.Json.Nodes;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using Xunit;

namespace DeepBrain.Tests;

public sealed class StrategyLearningTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"deepbrain-strategies-"+Guid.NewGuid().ToString("N"));
    public StrategyLearningTests()=>Directory.CreateDirectory(_dir);
    public void Dispose()=>Directory.Delete(_dir,true);
    private PlanningConfig Config=>new(){Enable=true,EnableStrategyLearning=true,Path=Path.Combine(_dir,"planning.json"),StallTicks=5,VerificationTicks=1};
    private ExecutivePlanner Planner(PlanningConfig? cfg=null){var p=new ExecutivePlanner();p.Configure(cfg??Config);return p;}
    private static ExecutiveObservation O(double energy=.5,double safety=.9)=>new("calm_baseline",1,new HomeostasisDto(energy,.2,.2,.1,safety),.5,.5,false);
    private static GoalDto[] Goals=>new[]{new GoalDto("restore_energy","energy",.9,.5,"test")};

    [Fact] public void MigrationKeepsOldIntentionAndDoesNotInventStrategyHistory()
    {
        var old=Planner(Config with{EnableStrategyLearning=false});var id=old.Begin(Goals,O())!.PlanId;old.Flush();
        var p=Planner();Assert.Equal(id,p.Begin(Goals,O())!.PlanId);
        Assert.Equal(0,p.Snapshot().StrategyLearning!.LearnedAttempts);Assert.NotNull(p.Snapshot().StrategyLearning!.Choice);
    }
    [Fact] public void UnavailableOrUnselectedMethodIsNotTaughtAsIneffective()
    {
        var p=Planner();for(int i=0;i<12;i++)
        {p.Begin(Goals,O());p.ApplyBias(new Dictionary<string,double>{{"focus_widen",.3}});p.Observe("focus_widen",O(),O(),false,0);}
        var s=p.Snapshot().StrategyLearning!;Assert.Equal(0,s.LearnedAttempts);
        Assert.Contains(s.RecentAttempts,a=>a.Diagnosis=="unavailable");
        Assert.DoesNotContain(s.RecentAttempts,a=>a.Diagnosis=="no_effect");
    }
    [Fact] public void AppliedBiasIsNotMistakenForExecutedStrategy()
    {
        var p=Planner();for(int i=0;i<12;i++)
        {p.Begin(Goals,O());p.ApplyBias(new Dictionary<string,double>{{"rest_short",.3},{"breathe_slow",.3}});p.Observe("focus_widen",O(),O(),false,0);}
        Assert.Equal(0,p.Snapshot().StrategyLearning!.LearnedAttempts);
        Assert.All(p.Snapshot().StrategyLearning!.RecentAttempts,a=>Assert.Equal("not_selected",a.Diagnosis));
    }
    [Fact] public void ConfirmedNoEffectSelectsUntriedMethodAndDoesNotCycleForever()
    {
        var p=Planner();var tried=new HashSet<string>();
        for(int i=0;i<18;i++)
        {var plan=p.Begin(Goals,O());if(plan is null)continue;tried.Add(p.Snapshot().StrategyLearning!.Choice!.Id);p.Observe(plan.PreferredAction!,O(),O(),false,0);}
        Assert.Equal(3,tried.Count);Assert.Equal(1,p.Snapshot().Failed);
        var s=p.Snapshot().StrategyLearning!;Assert.Equal(3,s.CompletedAttempts);Assert.Equal(3,s.LearnedAttempts);
        Assert.All(s.RecentAttempts,a=>Assert.Equal("no_effect",a.Diagnosis));
    }
    [Fact] public void InterruptedAttemptDoesNotPoisonLearnedSuccessRate()
    {
        var p=Planner();p.Begin(Goals,O());p.Observe("rest_short",O(),O(.55),false,0);
        p.Begin(Array.Empty<GoalDto>(),O() with{Scenario="fatigue_day",EpisodeId=2});
        var s=p.Snapshot().StrategyLearning!;Assert.Equal(0,s.LearnedAttempts);Assert.Equal("conditions_changed",Assert.Single(s.RecentAttempts).Diagnosis);
    }
    [Fact] public void ActualEffectAndTimeAreRecordedSeparatelyFromExpectedResult()
    {
        var p=Planner();p.Begin(Goals,O());p.Observe("rest_short",O(),O(.8),false,0);
        var s=p.Snapshot().StrategyLearning!;var r=Assert.Single(s.RecentAttempts);
        Assert.Equal("criterion_met",r.Diagnosis);Assert.Null(r.ExpectedProgressPerAction);
        Assert.InRange(r.ActualProgressPerAction!.Value,.249,.251);Assert.Equal(1,s.LearnedAttempts);
    }
    [Fact] public void ResumePreservesAttemptIdentityAndOnlyLearnsItOnce()
    {
        var p=Planner();var plan=p.Begin(Goals,O())!;p.Observe("rest_short",O(),O(.55),false,0);p.Flush();
        var before=JsonNode.Parse(File.ReadAllText(Config.Path))!;var id=before["active"]!["attempt"]!["id"]!.GetValue<string>();
        p=Planner();Assert.Equal(plan.PlanId,p.Begin(Goals,O(.55))!.PlanId);p.Observe("rest_short",O(.55),O(.8),false,0);p.Flush();
        var s=Planner().Snapshot().StrategyLearning!;Assert.Equal(1,s.LearnedAttempts);Assert.Equal(id,Assert.Single(s.RecentAttempts).Id);
    }
    [Fact] public void SleepFreezesAttemptAndNeverTrainsFromSleepingState()
    {
        var p=Planner();p.Begin(Goals,O());var clock=p.Snapshot().AwakeClock;
        for(int i=0;i<100;i++){p.Begin(Goals,O() with{Sleeping=true});p.Observe("rest_short",O(),O(.8),false,0);}
        Assert.Equal(clock,p.Snapshot().AwakeClock);Assert.Equal(0,p.Snapshot().StrategyLearning!.CompletedAttempts);
    }
    [Fact] public void RecoveryStrengthChangesOnlyTheSelectedValidActionAndSurvivesRestart()
    {
        var p=Planner();for(int i=0;i<6;i++){var plan=p.Begin(Goals,O())!;p.Observe(plan.PreferredAction!,O(),O(),false,0);}
        Assert.Equal("steady_rest",p.Snapshot().StrategyLearning!.Choice!.Id);
        var rest=new ActionDto("internal","rest_short",.2,null);Assert.Equal(.9,p.AdjustAction(rest,false).Strength);
        Assert.Equal(.2,p.AdjustAction(rest,true).Strength);
        var other=rest with{Name="explore_signal"};Assert.Equal(other,p.AdjustAction(other,false));
        p.Flush();p=Planner();p.Begin(Goals,O());Assert.Equal(.9,p.AdjustAction(rest,false).Strength);
    }
    [Fact] public void LowerIntensityCannotBeCreditedToTheStrongerMethod()
    {
        var state=new StrategyLearningState();var a=new StrategyAttempt{Id="low",Choice=new("steady_rest","rest","steady","initial_method",0,0,null){MinimumStrength=.9}};
        for(int i=0;i<3;i++)StrategyLearning.Observe(a,"restore_energy","rest_short",O(),O(.8),false,0,.2,.1);
        StrategyLearning.Finish(state,a,"plan","restore_energy","calm_baseline","succeeded","criterion_met",3);
        Assert.Equal(0,state.Learned);Assert.Equal("not_selected",Assert.Single(state.History).Diagnosis);
    }
    [Fact] public void ToggleKeepsHistoryAndCreatesANewAttemptIdentity()
    {
        var p=Planner();p.Begin(Goals,O());p.Configure(Config with{EnableStrategyLearning=false});p.Configure(Config);
        p.Begin(Goals,O());p.Observe("rest_short",O(),O(.8),false,0);p.Flush();
        var s=Planner().Snapshot().StrategyLearning!;Assert.Equal(2,s.CompletedAttempts);Assert.Equal(1,s.LearnedAttempts);
        Assert.Equal(2,s.RecentAttempts.Select(a=>a.Id).Distinct().Count());
    }
    [Fact] public void CorruptAttemptCannotEraseExistingLearning()
    {
        var p=Planner();p.Begin(Goals,O());p.Flush();var doc=JsonNode.Parse(File.ReadAllText(Config.Path))!;
        doc["active"]!["attempt"]!.AsObject().Remove("executions");var bad=doc.ToJsonString();File.WriteAllText(Config.Path,bad);
        Assert.Throws<JsonException>(()=>Planner());Assert.Equal(bad,File.ReadAllText(Config.Path));
    }
    [Fact] public void RegulatedRecoveryKeepsItsPhaseAcrossRestartAndUsesObservedArousal()
    {
        var high=O() with{Body=O().Body with{Arousal=.8}};var p=Planner();p.Begin(Goals,high);
        Assert.Equal("regulated_rest",p.Snapshot().StrategyLearning!.Choice!.Id);
        Assert.Equal("breathe_slow",p.Snapshot().Plan!.PreferredAction);p.Flush();
        p=Planner();p.Begin(Goals,high);Assert.Equal("breathe_slow",p.Snapshot().Plan!.PreferredAction);
        p.Begin(Goals,O());Assert.Equal("rest_short",p.Snapshot().Plan!.PreferredAction);
        Assert.Equal(.9,p.AdjustAction(new("internal","rest_short",.1,null),false).Strength);
    }
    [Fact] public void UrgentSafetyAndActionMasksStillOverrideLearnedRecovery()
    {
        var p=Planner();p.Begin(Goals,O());p.Begin(Goals,O(safety:.1));
        Assert.Equal("regulate",p.Snapshot().Plan!.GoalId);
        var scores=new Dictionary<string,double>{{"breathe_slow",double.NegativeInfinity},{"focus_widen",.2}};
        p.ApplyBias(scores);Assert.True(double.IsNegativeInfinity(scores["breathe_slow"]));
        var rest=new ActionDto("internal","rest_short",.2,null);Assert.Equal(rest,p.AdjustAction(rest,false));
        Assert.Equal(0,p.Snapshot().StrategyLearning!.LearnedAttempts);
    }
    [Fact] public void CorruptLearnedModelCannotResetStatistics()
    {
        var p=Planner();p.Begin(Goals,O());p.Observe("rest_short",O(),O(.8),false,0);p.Flush();
        var doc=JsonNode.Parse(File.ReadAllText(Config.Path))!;doc["learning"]!["models"]!.AsObject().First().Value!["samples"]=-1;
        var bad=doc.ToJsonString();File.WriteAllText(Config.Path,bad);
        Assert.Throws<InvalidDataException>(()=>Planner());Assert.Equal(bad,File.ReadAllText(Config.Path));
    }
    [Fact] public void InvalidExecutionContaminatesTheAttemptInsteadOfTrainingIt()
    {
        var p=Planner();p.Begin(Goals,O());p.Observe("rest_short",O(),O(.6),true,0);
        p.Begin(Goals,O(.6));p.Observe("rest_short",O(.6),O(.8),false,0);
        var s=p.Snapshot().StrategyLearning!;Assert.Equal(0,s.LearnedAttempts);
        Assert.Equal("invalid_action",Assert.Single(s.RecentAttempts).Diagnosis);
    }
    [Fact] public void RestartDoesNotChargeTheAttemptForResetPhysiology()
    {
        var p=Planner();p.Begin(Goals,O());p.Observe("rest_short",O(),O(.55),false,0);p.Flush();
        p=Planner();p.Begin(Goals,O(.2));p.Observe("rest_short",O(.2),O(.24),false,0);
        p.Begin(Goals,O(.24));p.Observe("rest_short",O(.24),O(.8),false,0);
        Assert.Equal(0,Assert.Single(p.Snapshot().StrategyLearning!.RecentAttempts).EnergySpent);
    }
    [Fact] public void LearnedPreferenceIsContextSpecificAndUsesObservedResults()
    {
        var state=new StrategyLearningState();var clock=0L;
        for(int i=0;i<12;i++)
        {
            var rest=new StrategyAttempt{Id="bad"+i,Started=clock,Choice=new("rest","rest","steady","initial_method",0,0,null)};
            for(int j=0;j<3;j++)StrategyLearning.Observe(rest,"restore_energy","rest_short",O(),O(),false,0,.2);
            StrategyLearning.Finish(state,rest,"bad"+i,"restore_energy","calm_baseline","failed","no_progress",++clock);
            var good=new StrategyAttempt{Id="good"+i,Started=clock,Choice=new("steady_rest","steady rest","steady","initial_method",0,0,null)};
            for(int j=0;j<2;j++)StrategyLearning.Observe(good,"restore_energy","rest_short",O(),O(.55),false,0,.2);
            StrategyLearning.Observe(good,"restore_energy","rest_short",O(),O(.8),false,0,.2);
            StrategyLearning.Finish(state,good,"good"+i,"restore_energy","calm_baseline","succeeded","criterion_met",++clock);
        }
        var choice=StrategyLearning.Choose(state,"next","restore_energy",O(),clock,1,new HashSet<string>(),Array.Empty<StrategyEffectHint>());
        Assert.Equal("steady_rest",choice.Choice.Id);Assert.Equal("learned_results",choice.Choice.Reason);Assert.True(choice.Choice.Confidence>0);
        var other=StrategyLearning.Choose(state,"other","restore_energy",O(.2),clock,1,new HashSet<string>(),Array.Empty<StrategyEffectHint>());
        Assert.Equal("rest",other.Choice.Id);Assert.Equal(0,other.Choice.Samples);
    }
}
