using System.Reflection;
using System.Text.Json;
using DeepBrain.Host.BrainLife;
using DeepBrain.Shared.Brain;
using Xunit;

namespace DeepBrain.Tests;

public sealed class StrategyBenchmarkTests
{
    private sealed record Score(string Scenario,int Seed,bool Adaptive,long Succeeded,long Failed,long Interrupted,
        int UnsafeTicks,int Replans,double MeanEnergy,long LearnedAttempts,Dictionary<string,int> GoalResults,
        Dictionary<string,int> Diagnoses);
    [Fact] public void PairedLifeLoopBenchmarkUsesSameScenariosAndSeeds()
    {
        var dir=Path.Combine(Path.GetTempPath(),"deepbrain-strategy-benchmark-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var scores=new List<Score>();
        var diagnostics=new List<object>();
        try
        {
            foreach(var scenario in BrainConfig.Default.Scenarios.Keys.Order())
            foreach(var seed in (Environment.GetEnvironmentVariable("DEEPBRAIN_STRATEGY_BENCHMARK_SEEDS")??"2027,4099,8191").Split(',').Select(int.Parse))
            foreach(var adaptive in new[]{false,true})
            {
                var folder=Path.Combine(dir,scenario+seed+adaptive);Directory.CreateDirectory(folder);
                var config=BrainConfig.Default with { ScenarioDefault=scenario,CurriculumMode="fixed",
                    Curriculum=BrainConfig.Default.Curriculum with{Mode="fixed"},
                    Planning=new PlanningConfig{Enable=true,EnableStrategyLearning=adaptive,Path=Path.Combine(folder,"planning.json"),SaveEveryTicks=1000},
                    CognitiveDynamics=new CognitiveDynamicsConfig{Enable=true,Path=Path.Combine(folder,"cognition.json"),SaveEveryTicks=1000},
                    Ml=BrainConfig.Default.Ml with{Enable=false,Backend="off",Seed=seed},
                    Memory=MemoryConfig.Default with{Enable=false},Habits=HabitConfig.Default with{Enable=false,ImportPath=""},
                    Llm=LlmConfig.Default with{Enable=false,PersistJournal=false},
                    Evaluation=BrainConfig.Default.Evaluation with{SaveReports=false} };
                var file=Path.Combine(folder,"config.json");File.WriteAllText(file,JsonSerializer.Serialize(config));
                LifeStateDto? latest=null,warm=null;int unsafeTicks=0,replans=0,count=0;double energy=0;
                var seen=new Dictionary<string,int>();
                var outcomeIds=new HashSet<string>();var goalResults=new Dictionary<string,int>();
                object? decisionTrace=null;
                var loop=new LifeLoop(new WorldSim(seed),new HomeostasisEngine(),new InstinctEngine(),new EmotionEngine(),
                    new ActionSelector(),new Actuator(),new RewardEngine(),new LearningEngine(),new BrainConfigLoader(file,_=>{}),
                    (s,_)=>{latest=s;
                        if(scenario=="fatigue_day" && seed==2027 && count<800)diagnostics.Add(new{adaptive,s.Tick,s.LastDecision,s.Homeostasis,Plan=s.Executive!.Plan,Choice=s.Executive.StrategyLearning!.Choice,Decision=decisionTrace});
                        foreach(var result in s.Executive!.RecentOutcomes)
                        if(outcomeIds.Add(result.PlanId) && count>=3000){var key=result.GoalId+"/"+result.Status;goalResults[key]=goalResults.GetValueOrDefault(key)+1;}
                        if(count++<3000){warm=s;return;}energy+=s.Homeostasis.Energy;
                        if(s.Homeostasis.Safety<.35||s.Homeostasis.Pain>.65)unsafeTicks++;
                        if(s.Executive?.Plan is {} p){var old=seen.GetValueOrDefault(p.PlanId!);replans+=Math.Max(0,p.Replans-old);seen[p.PlanId!]=p.Replans;}},
                    (t,_)=>{if(t.Stage=="decision")decisionTrace=t.Data;},(_,_)=>{},_=>{});
                var tick=typeof(LifeLoop).GetMethod("TickOnce",BindingFlags.NonPublic|BindingFlags.Instance)!;
                for(int i=0;i<6000;i++)tick.Invoke(loop,new object[]{.2,CancellationToken.None});loop.FlushPersistentState();
                var e=latest!.Executive!;var w=warm!.Executive!;
                scores.Add(new(scenario,seed,adaptive,e.Succeeded-w.Succeeded,e.Failed-w.Failed,e.Interrupted-w.Interrupted,
                    unsafeTicks,replans,energy/3000,e.StrategyLearning!.LearnedAttempts,goalResults,
                    e.StrategyLearning.RecentAttempts.GroupBy(a=>a.Diagnosis).ToDictionary(g=>g.Key,g=>g.Count())));
                Assert.Null(e.LastError);Assert.Null(latest.Cognition!.LastError);
                if(adaptive)Assert.True(e.StrategyLearning.LearnedAttempts>0);
            }
            var output=Environment.GetEnvironmentVariable("DEEPBRAIN_STRATEGY_BENCHMARK_OUTPUT");
            if(!string.IsNullOrWhiteSpace(output))
            {File.WriteAllText(output,JsonSerializer.Serialize(scores,new JsonSerializerOptions{WriteIndented=true}));File.WriteAllText(output+".diagnostics.json",JsonSerializer.Serialize(diagnostics));}
            Assert.Equal(36,scores.Count);
            var baseline=scores.Where(s=>!s.Adaptive).ToArray();var adaptiveScores=scores.Where(s=>s.Adaptive).ToArray();
            Assert.True(adaptiveScores.Sum(s=>s.Succeeded)>baseline.Sum(s=>s.Succeeded),"Adaptive strategy did not improve completed goals");
            Assert.True(adaptiveScores.Sum(s=>s.Failed)<baseline.Sum(s=>s.Failed),"Adaptive strategy did not reduce failures");
            Assert.True(adaptiveScores.Sum(s=>s.UnsafeTicks)<=baseline.Sum(s=>s.UnsafeTicks),"Safety regression");
        }
        finally{Directory.Delete(dir,true);}
    }
}
