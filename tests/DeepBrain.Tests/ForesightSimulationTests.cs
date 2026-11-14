using System.Reflection;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using Xunit;

namespace DeepBrain.Tests;

public sealed class ForesightSimulationTests
{
    [Fact] public void ShortHorizonPlanningIsMeasuredAgainstTheSameSeededScenarios()
    {
        var dir=Path.Combine(Path.GetTempPath(),"deepbrain-foresight-sim-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var scores=new List<Score>();
        try
        {
            foreach(var scenario in BrainConfig.Default.Scenarios.Keys.Order())
            foreach(var seed in new[]{2693,12007})
            foreach(var enabled in new[]{false,true})
            {
                var folder=Path.Combine(dir,scenario+seed+enabled);Directory.CreateDirectory(folder);
                var config=BrainConfig.Default with {
                    ScenarioDefault=scenario,CurriculumMode="fixed",Curriculum=BrainConfig.Default.Curriculum with {Mode="fixed"},
                    Planning=new(){Enable=true,EnableStrategyLearning=true,EnableIntentions=true,Path=Path.Combine(folder,"planning.json"),SaveEveryTicks=1000},
                    CognitiveDynamics=new(){Enable=true,Path=Path.Combine(folder,"cognition.json"),SaveEveryTicks=1000},
                    Foresight=new(enabled),Ml=BrainConfig.Default.Ml with {Enable=false,Backend="off",Seed=seed},
                    Memory=MemoryConfig.Default with {Enable=false},Habits=HabitConfig.Default with {Enable=false,ImportPath=""},
                    Llm=LlmConfig.Default with {Enable=false,PersistJournal=false},Evaluation=BrainConfig.Default.Evaluation with {SaveReports=false}
                };
                var file=Path.Combine(folder,"config.json");File.WriteAllText(file,JsonSerializer.Serialize(config));
                LifeStateDto? latest=null;int unsafeTicks=0,loopTicks=0,multiStep=0,changed=0;double energy=0;
                var loop=new LifeLoop(new WorldSim(seed),new HomeostasisEngine(),new InstinctEngine(),new EmotionEngine(),
                    new ActionSelector(),new Actuator(),new RewardEngine(),new LearningEngine(),new BrainConfigLoader(file,_=>{}),
                    (s,_)=>{
                        latest=s;energy+=s.Homeostasis.Energy;
                        if(s.Homeostasis.Safety<.35||s.Homeostasis.Pain>.65)unsafeTicks++;
                        if(s.LoopInfo?.IsInLoop==true)loopTicks++;
                        if(s.Foresight is { } f)
                        {
                            if(f.Options.Any(o=>o.Steps.Count>1))multiStep++;
                            if(f.AppliedBias>0)changed++;
                            Assert.All(f.Options,o=>Assert.InRange(o.Steps.Count,0,3));
                            Assert.InRange(f.AppliedBias,0,.12);Assert.InRange(f.LearnedModels,0,192);
                            Assert.Null(s.Cognition!.LastError);Assert.Null(s.Executive!.LastError);
                        }
                    },(_,_)=>{},(_,_)=>{},_=>{});
                var tick=typeof(LifeLoop).GetMethod("TickOnce",BindingFlags.NonPublic|BindingFlags.Instance)!;
                for(var i=0;i<2500;i++)tick.Invoke(loop,new object[]{.2,CancellationToken.None});
                loop.FlushPersistentState();var e=latest!.Executive!;var f=latest.Foresight;
                scores.Add(new(scenario,seed,enabled,e.Succeeded,e.Failed,e.Interrupted,unsafeTicks,loopTicks,energy/2500,
                    multiStep,changed,f?.CheckedSteps??0,f?.CompletedPlans??0,f?.MeanError));
            }
            var output=Environment.GetEnvironmentVariable("DEEPBRAIN_FORESIGHT_BENCHMARK_OUTPUT");
            if(!string.IsNullOrWhiteSpace(output))File.WriteAllText(output,JsonSerializer.Serialize(scores,new JsonSerializerOptions{WriteIndented=true}));
            Assert.Equal(24,scores.Count);
            Assert.True(scores.Where(x=>x.Enabled).Sum(x=>x.MultiStepTicks)>0);
            Assert.True(scores.Where(x=>x.Enabled).Sum(x=>x.CompletedForecasts)>0);
            Assert.All(scores.Where(x=>x.Enabled),x=>Assert.InRange(x.MeanError??1,0,.08));
            Assert.True(scores.Where(x=>x.Enabled).Sum(x=>x.UnsafeTicks)<=scores.Where(x=>!x.Enabled).Sum(x=>x.UnsafeTicks),"Safety regression");
        }
        finally {Directory.Delete(dir,true);}
    }
    private sealed record Score(string Scenario,int Seed,bool Enabled,long Succeeded,long Failed,long Interrupted,
        int UnsafeTicks,int LoopTicks,double MeanEnergy,int MultiStepTicks,int BiasTicks,long CheckedSteps,long CompletedForecasts,double? MeanError);
}
