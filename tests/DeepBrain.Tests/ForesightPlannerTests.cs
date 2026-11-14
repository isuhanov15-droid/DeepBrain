using System.Reflection;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class ForesightPlannerTests
{
    private static readonly ActionDto[] Actions = { new("internal","rest_short",1,null),new("external","explore_signal",1,null),new("internal","focus_widen",1,null) };
    private static readonly ActionsConfig NoCooldown = BrainConfig.Default.Actions with { RestShort=0, ExploreSignal=0, FocusWiden=0 };
    private static ForecastObservation Observation(long tick=1000,double energy=.55) => new("test",1,"plan","explore",new(energy,.3,.3,.05,.9),new("calm",-.1,.3),.6,.3,false,tick);
    private static Dictionary<string,double> Scores() => Actions.ToDictionary(a=>a.Name,_=>.5);
    private static void Plan(ForesightPlanner p,ForecastObservation o,Dictionary<string,double>? scores=null,ActionCooldowns? cooldowns=null,ActionsConfig? limits=null,int horizon=3) =>
        p.Plan(o,Actions,scores??Scores(),cooldowns??new(),limits??NoCooldown,60,false,.1,Array.Empty<LearnedActionEffect>(),horizon);
    private static JsonElement Capture(ForesightPlanner p) => (JsonElement)typeof(ForesightPlanner).GetMethod("CaptureRuntimeState",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(p,null)!;
    private static void Restore(ForesightPlanner p,JsonElement state) => typeof(ForesightPlanner).GetMethod("RestoreRuntimeState",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(p,new object[]{state});
    private static ForesightPlanner Trained(double energy=.55,double explorationCost=.06)
    {
        var p=new ForesightPlanner();p.Configure(new(true));
        // Recorded outcomes in a controlled toy world; no production data is used.
        foreach(var action in Actions)
        for(var i=0;i<80;i++)
        {
            var before=Observation(i,energy);
            var after=action.Name switch {
                "rest_short"=>before with { Body=before.Body with {Energy=Math.Min(1,energy+.04),Fatigue=.27} },
                "explore_signal"=>before with { Body=before.Body with {Energy=Math.Max(0,energy-explorationCost)},Novelty=.57 },
                _=>before };
            p.Observe(action,before,after,false);
        }
        for(var i=80;i<160;i++)
        {
            var o=Observation(i,energy);Plan(p,o);
            p.Observe(Actions[2],o,o,false); // Measured zero drift between successive decisions.
        }
        p.Suspend("test_boundary");return p;
    }

    [Fact] public void UnknownModelsKeepNormalScoresAndDoNotInventPredictions()
    {
        var p=new ForesightPlanner();p.Configure(new(true));var scores=Scores();Plan(p,Observation(),scores);
        Assert.All(scores.Values,v=>Assert.Equal(.5,v));
        Assert.Equal("learning",p.Snapshot().Status);
        Assert.All(p.Snapshot().Options,o=>{Assert.Equal("unknown",o.Status);Assert.Empty(o.Steps);});
        Assert.Equal(0,p.Snapshot().CheckedSteps);
    }
    [Fact] public void BoundedLookaheadUsesMultipleStepsWithoutLearningFromImaginedActions()
    {
        var p=Trained();var before=Capture(p).GetProperty("models").GetRawText();
        Plan(p,Observation());var result=p.Snapshot();
        Assert.Contains(result.Options,o=>o.Steps.Count==3);
        Assert.All(result.Options,o=>Assert.InRange(o.Steps.Count,1,3));
        Assert.Equal(before,Capture(p).GetProperty("models").GetRawText());
        Assert.InRange(result.AppliedBias,0,.12);
    }
    [Fact] public void PreparationIsComparedWithExplorationWhenEnergyReserveIsSmall()
    {
        // Here exploration alone would cross the measured energy reserve; rest
        // makes exploration possible within the same short horizon.
        var p=Trained(.25,.08);Plan(p,Observation(1000,.25));
        var options=p.Snapshot().Options;
        Assert.Contains(options,o=>o.FirstAction=="rest_short"&&o.Steps.Count>1);
        Assert.Equal("rest_short",p.Snapshot().RecommendedAction);
        Assert.All(options.Where(o=>o.Status=="evaluated"),o=>Assert.Equal(0,o.Risk));
        Assert.All(options.SelectMany(o=>o.Steps.Skip(1)),s=>Assert.True(s.Expected.Body.Energy>=.18));
    }
    [Fact] public void RootMasksCooldownsAndUrgentRecoveryCannotBeOverridden()
    {
        var p=Trained();var scores=Scores();scores["rest_short"]=double.NegativeInfinity;
        var cd=new ActionCooldowns();cd.Mark("explore_signal",1000);
        Plan(p,Observation(),scores,cd,NoCooldown with {ExploreSignal=5});
        Assert.DoesNotContain(p.Snapshot().Options,o=>o.FirstAction=="rest_short");
        Assert.DoesNotContain(p.Snapshot().Options.SelectMany(o=>o.Steps),s=>s.Action=="explore_signal");
        Assert.True(double.IsNegativeInfinity(scores["rest_short"]));
        scores=Scores();Plan(p,Observation() with {Body=Observation().Body with {Safety=.2}},scores);
        Assert.Equal("urgent_recovery",p.Snapshot().Status);Assert.All(scores.Values,v=>Assert.Equal(.5,v));
    }
    [Fact] public void RepeatingAnActionInTheFutureRespectsItsCooldown()
    {
        var p=Trained();Plan(p,Observation(),limits:NoCooldown with {ExploreSignal=3,RestShort=3,FocusWiden=3});
        foreach(var o in p.Snapshot().Options)Assert.Equal(o.Steps.Count,o.Steps.Select(s=>s.Action).Distinct().Count());
    }
    [Fact] public void ActualResultIsCheckedAndActiveForecastSurvivesRestartExactlyOnce()
    {
        var p=Trained();var before=Observation();Plan(p,before);
        var option=p.Snapshot().Options.First(o=>o.Steps.Count==3);
        var step=option.Steps[0];var after=FromExpected(before,step.Expected);
        p.Observe(new("internal",step.Action,step.Strength,null),before,after,false);
        var saved=Capture(p);var restored=new ForesightPlanner();restored.Configure(new(true));Restore(restored,saved);
        Assert.Equal(saved.GetRawText(),Capture(restored).GetRawText());
        var count=restored.Snapshot().CheckedSteps;
        var next=after with {Tick=1001};Plan(restored,next);var planned=option.Steps[1];
        restored.Observe(new("internal",planned.Action,planned.Strength,null),next,FromExpected(next,planned.Expected),false);
        Assert.Equal(count+1,restored.Snapshot().CheckedSteps);
        Assert.Equal(2,restored.Snapshot().ActiveForecast!.CheckedSteps);
    }
    [Fact] public void ContextChangeAndSleepRetireTheConditionalForecast()
    {
        var p=Trained();var o=Observation();Plan(p,o);var option=p.Snapshot().Options.First(x=>x.Steps.Count>1);
        var step=option.Steps[0];p.Observe(new("internal",step.Action,step.Strength,null),o,FromExpected(o,step.Expected),false);
        Plan(p,o with {EpisodeId=2,Tick=1001});Assert.Null(p.Snapshot().ActiveForecast);
        Assert.Equal("context_changed",p.Snapshot().RecentChecks[0].Status);
        p.Suspend("sleeping");Assert.Empty(p.Snapshot().Options);Assert.Equal("sleeping",p.Snapshot().Status);
    }
    [Fact] public void LargePredictionErrorStopsThePathAndIsRecordedAsMeasuredError()
    {
        var p=Trained();var o=Observation();Plan(p,o);var option=p.Snapshot().Options.First(x=>x.Steps.Count>1);
        var step=option.Steps[0];p.Observe(new("internal",step.Action,step.Strength,null),o,o with {Body=new(.1,.9,.9,.7,.2)},false);
        Assert.Null(p.Snapshot().ActiveForecast);Assert.Equal("prediction_mismatch",p.Snapshot().RecentChecks[0].Status);
        Assert.True(p.Snapshot().RecentChecks[0].MeanError>.08);
    }
    private static ForecastObservation FromExpected(ForecastObservation before,ForecastStateDto state) => before with
        {Body=state.Body,Novelty=state.Novelty,Agency=state.Agency,Affect=new("calm",state.Valence,state.Arousal),SocialPending=state.SocialPending};
}
