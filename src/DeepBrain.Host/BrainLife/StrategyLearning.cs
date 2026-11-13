using System.Text.Json.Serialization;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed record StrategyEffectHint(string GoalId, string Action, double Progress, double Confidence);

// Stored inside the planner's atomic checkpoint: intention, attempt and statistics commit together.
public sealed class StrategyLearningState
{
    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public long Completed { get; set; }
    [JsonRequired] public long Learned { get; set; }
    [JsonRequired] public Dictionary<string, StrategyStatistics> Models { get; set; } = new();
    [JsonRequired] public List<StrategyAttemptDto> History { get; set; } = new();
}
public sealed class StrategyStatistics
{
    [JsonRequired] public long Samples { get; set; }
    [JsonRequired] public long Successes { get; set; }
    [JsonRequired] public double MeanProgress { get; set; }
    [JsonRequired] public double MeanCost { get; set; }
    [JsonRequired] public double MeanTicks { get; set; }
    [JsonRequired] public long Updated { get; set; }
}
public sealed class StrategyAttempt
{
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public StrategyChoiceDto Choice { get; set; } = null!;
    [JsonRequired] public string Phase { get; set; } = "main";
    [JsonRequired] public long Started { get; set; }
    [JsonRequired] public int Observations { get; set; }
    [JsonRequired] public int Executions { get; set; }
    [JsonRequired] public int Unavailable { get; set; }
    [JsonRequired] public int Other { get; set; }
    [JsonRequired] public int Invalid { get; set; }
    [JsonRequired] public double Progress { get; set; }
    [JsonRequired] public double EnergySpent { get; set; }
    [JsonRequired] public double? LastEnergy { get; set; }
}

public static class StrategyLearning
{
    private sealed record Method(string Id, string Goal, string Title, string Main, double MinimumStrength = 0);
    private static readonly Method[] Methods = {
        new("rest", "restore_energy", "Восстановление коротким отдыхом", "rest_short"),
        new("steady_rest", "restore_energy", "Устойчивое восстановление сил", "rest_short", .90),
        new("regulated_rest", "restore_energy", "Снизить возбуждение, затем восстановить силы", "rest_short", .90),
        new("breathe", "regulate", "Стабилизация дыханием", "breathe_slow"),
        new("rest_support", "regulate", "Снизить нагрузку отдыхом", "rest_short"),
        new("explore", "explore", "Прямое исследование", "explore_signal"),
        new("orient_explore", "explore", "Расширить внимание к обстановке", "focus_widen"),
        new("contact", "connect", "Ответ на текущий контакт", "emit_message"),
        new("orient_contact", "connect", "Расширить внимание к контакту", "focus_widen"),
        new("focus", "focus", "Сосредоточение", "focus_narrow"),
        new("broaden_focus", "focus", "Перестроить фокус через обзор", "focus_widen")
    };
    private static string Key(string scenario, string goal, string context, string method) => scenario+"/"+goal+"/"+context+"/"+method;
    public static bool ValidMethod(string goal, string id) => Methods.Any(m=>m.Goal==goal && m.Id==id);
    public static int MethodCount(string goal) => Methods.Count(m=>m.Goal==goal);
    public static double MinimumStrength(StrategyAttempt attempt) => attempt.Choice.Id=="regulated_rest" && attempt.Phase=="settle"
        ? .65 : Methods.Single(m=>m.Id==attempt.Choice.Id).MinimumStrength;
    public static void UpdateObservation(StrategyAttempt attempt, ExecutiveObservation observation)
    {
        if(attempt.Choice.Id!="regulated_rest")return;
        if(observation.Body.Energy<.30)attempt.Phase="main";
        else if(observation.Body.Arousal>.55)attempt.Phase="settle";
        else if(observation.Body.Arousal<=.35)attempt.Phase="main";
    }
    public static string Context(ExecutiveObservation o) => o.Body.Energy<.35 || o.Body.Fatigue>.75 ? "depleted" :
        o.Body.Arousal>.5 || o.Body.Pain>.4 || o.Body.Safety<.5 ? "strained" : "steady";
    public static string Action(StrategyAttempt attempt)
    {
        var m=Methods.Single(m=>m.Id==attempt.Choice.Id);
        return m.Id=="regulated_rest" && attempt.Phase=="settle" ? "breathe_slow" : m.Main;
    }
    public static string CandidateStrategy(StrategyAttempt attempt) => Action(attempt) switch
    { "rest_short"=>"rest", "breathe_slow"=>"regulate", "explore_signal"=>"explore", "emit_message"=>"connect", _=>"focus" };
    public static StrategyAttempt Choose(StrategyLearningState state, string planId, string goal,
        ExecutiveObservation o, long clock, int revision, IReadOnlySet<string> tried,
        IReadOnlyList<StrategyEffectHint> hints)
    {
        var context=Context(o);var candidates=Methods.Where(m=>m.Goal==goal).ToArray();
        var ranked=candidates.Select((m,index)=>
        {
            state.Models.TryGetValue(Key(o.Scenario,goal,context,m.Id),out var model);
            var confidence=model is null?0:Math.Min(.9,model.Samples/(model.Samples+5.0));
            var action=m.Id=="regulated_rest" && o.Body.Arousal>.35?"breathe_slow":m.Main;
            var hint=hints.FirstOrDefault(h=>h.GoalId==goal && h.Action==action && double.IsFinite(h.Progress) && double.IsFinite(h.Confidence));
            var prior=m.Id=="regulated_rest"?(o.Body.Arousal>.55?.25:.07):index==0?.15:.10;
            var measured=model is null?0:.25*Math.Clamp(model.MeanProgress/.05,-1,1)+
                .55*(model.Successes+1.0)/(model.Samples+2.0)-.10*Math.Clamp(model.MeanTicks/180,0,1)-.15*Math.Clamp(model.MeanCost/.02,0,1);
            var score=prior*(1-confidence)+measured*confidence+
                (hint is null?0:.04*Math.Clamp(hint.Progress/.05,-1,1)*Math.Clamp(hint.Confidence,0,.9))+
                (tried.Contains(m.Id)?-.05:tried.Count>0 && (model?.Samples??0)<3?.05:0);
            return (Method:m,Model:model,Confidence:confidence,Hint:hint,Score:score,Tried:tried.Contains(m.Id));
        }).OrderByDescending(x=>x.Score).ThenBy(x=>x.Method.Id,StringComparer.Ordinal).First();
        var reason=ranked.Model is { Samples: >=3 } ? "learned_results" : ranked.Hint is not null ? "learned_effects" : "initial_method";
        if(tried.Count>0)reason=ranked.Tried?"best_remaining_evidence":"untried_alternative";
        return new StrategyAttempt { Id=planId+":"+revision,Started=clock,LastEnergy=o.Body.Energy,
            Phase=ranked.Method.Id=="regulated_rest" && o.Body.Arousal>.35 && o.Body.Energy>=.30?"settle":"main",
            Choice=new(ranked.Method.Id,ranked.Method.Title,context,reason,ranked.Model?.Samples??0,ranked.Confidence,
                ranked.Model is { Samples: >=3 } ? ranked.Model.MeanProgress : null) { MinimumStrength=ranked.Method.MinimumStrength } };
    }
    public static void Observe(StrategyAttempt a,string goal,string action,ExecutiveObservation before,
        ExecutiveObservation after,bool invalid,int handled,double focusTarget,double strength=1)
    {
        a.Observations++;
        a.EnergySpent+=Math.Max(0,(a.LastEnergy??before.Body.Energy)-after.Body.Energy);
        a.LastEnergy=after.Body.Energy;
        if(invalid || !double.IsFinite(strength) || strength<0 || strength>1){a.Invalid++;return;}
        if(action!=Action(a) || strength+1e-9<MinimumStrength(a)){a.Other++;return;}
        a.Executions++;
        a.Progress+=Progress(goal,before,after,handled,focusTarget);
    }
    private static double Progress(string goal,ExecutiveObservation before,ExecutiveObservation after,int handled,double focusTarget)
    {
        double Distance(ExecutiveObservation o)=>goal switch {
            "restore_energy"=>Math.Max(0,.75-o.Body.Energy)+Math.Max(0,o.Body.Fatigue-.25),
            "regulate"=>Math.Max(0,.70-o.Body.Safety)+Math.Max(0,o.Body.Pain-.20)+Math.Max(0,o.Body.Arousal-.35),
            "focus"=>Math.Max(0,o.AgencyNeed-focusTarget),_=>0};
        return Math.Clamp(goal switch {"connect"=>handled>0?1:0,"explore"=>before.Novelty-after.Novelty,_=>Distance(before)-Distance(after)},-1,1);
    }
    public static void Finish(StrategyLearningState state,StrategyAttempt a,string planId,string goal,
        string scenario,string outcome,string reason,long clock)
    {
        // A persisted active attempt and the model update share the same atomic planner write.
        if(state.History.Any(h=>h.Id==a.Id))return;
        var elapsed=(int)Math.Clamp(clock-a.Started+1,0,int.MaxValue);
        var interrupted=outcome=="interrupted";
        var diagnosis=interrupted?"conditions_changed":a.Invalid>0?"invalid_action":
            a.Executions==0?(a.Unavailable>=a.Observations && a.Observations>0?"unavailable":"not_selected"):
            outcome=="succeeded" && a.Progress>0?"criterion_met":
            a.Executions<Math.Max(1,a.Observations*.1)?"rarely_selected":outcome=="succeeded"?"criterion_met_other":
            a.Progress<=.00001?"no_effect":reason=="deadline"?"insufficient_time":"insufficient_progress";
        var eligible=!interrupted && a.Executions>0 && diagnosis is not ("rarely_selected" or "invalid_action" or "criterion_met_other") &&
            (a.Executions>=2 || outcome=="succeeded");
        state.Completed++;
        if(eligible)
        {
            var key=Key(scenario,goal,a.Choice.Context,a.Choice.Id);
            if(!state.Models.TryGetValue(key,out var model))
            {
                if(state.Models.Count>=256)state.Models.Remove(state.Models.MinBy(x=>x.Value.Updated).Key);
                model=new();state.Models[key]=model;
            }
            model.Samples++;if(outcome=="succeeded")model.Successes++;
            var rate=1.0/Math.Min(model.Samples,50);
            model.MeanProgress+=rate*(a.Progress/a.Executions-model.MeanProgress);
            model.MeanCost+=rate*(a.EnergySpent/Math.Max(1,a.Observations)-model.MeanCost);
            model.MeanTicks+=rate*(elapsed-model.MeanTicks);model.Updated=clock;state.Learned++;
        }
        state.History.Add(new(a.Id,planId,goal,scenario,a.Choice.Id,a.Choice.Title,a.Choice.Context,outcome,diagnosis,
            elapsed,a.Executions,a.Unavailable,a.Other,a.Invalid,a.Choice.ExpectedProgressPerAction,
            a.Executions>0?a.Progress/a.Executions:null,a.EnergySpent,DateTimeOffset.UtcNow));
        if(state.History.Count>40)state.History.RemoveAt(0);
    }
    public static void Validate(StrategyLearningState state,long clock)
    {
        if(state.SchemaVersion!=1 || state.Completed<0 || state.Learned<0 || state.Learned>state.Completed ||
            state.Models is null || state.History is null || state.Models.Count>256 || state.History.Count>40 ||
            state.History.Select(h=>h?.Id).Distinct().Count()!=state.History.Count ||
            state.Models.Any(p=>p.Value is null || !Methods.Any(m=>p.Key.EndsWith("/"+m.Goal+"/steady/"+m.Id) || p.Key.EndsWith("/"+m.Goal+"/strained/"+m.Id) || p.Key.EndsWith("/"+m.Goal+"/depleted/"+m.Id)) ||
                p.Value.Samples<=0 || p.Value.Successes<0 || p.Value.Successes>p.Value.Samples || p.Value.Updated<0 || p.Value.Updated>clock ||
                !Finite(p.Value.MeanProgress,-1,1)||!Finite(p.Value.MeanCost,0,1)||!Finite(p.Value.MeanTicks,0,int.MaxValue)) ||
            state.History.Any(h=>h is null || string.IsNullOrWhiteSpace(h.Id) || !ValidMethod(h.GoalId,h.StrategyId) ||
                h.AwakeTicks<0 || h.Executions<0 || h.UnavailableTicks<0 || h.OtherActions<0 || h.InvalidActions<0 ||
                h.Context is not ("steady" or "strained" or "depleted") || h.Outcome is not ("stalled" or "succeeded" or "failed" or "interrupted") ||
                h.ExpectedProgressPerAction.HasValue && !Finite(h.ExpectedProgressPerAction.Value,-1,1) ||
                !Finite(h.EnergySpent,0,int.MaxValue) || h.ActualProgressPerAction.HasValue && !Finite(h.ActualProgressPerAction.Value,-1,1)))
            throw new InvalidDataException("Invalid strategy learning history");
    }
    public static void ValidateAttempt(StrategyAttempt a,string goal,long clock)
    {
        if(a.Choice is null || !ValidMethod(goal,a.Choice.Id) || string.IsNullOrWhiteSpace(a.Id) || a.Started<0 || a.Started>clock ||
            a.Observations<0 || a.Executions<0 || a.Other<0 || a.Invalid<0 || a.Unavailable<0 ||
            a.Executions+a.Other+a.Invalid!=a.Observations ||
            !Finite(a.Progress,-a.Executions,a.Executions) || !Finite(a.EnergySpent,0,a.Observations) ||
            a.LastEnergy.HasValue && !Finite(a.LastEnergy.Value,0,1) ||
            a.Choice.Context is not ("steady" or "strained" or "depleted") || !Finite(a.Choice.Confidence,0,.9) || a.Choice.Samples<0 ||
            a.Choice.MinimumStrength!=Methods.Single(m=>m.Id==a.Choice.Id).MinimumStrength ||
            a.Phase is not ("main" or "settle") || a.Phase=="settle" && a.Choice.Id!="regulated_rest" ||
            string.IsNullOrWhiteSpace(a.Choice.Title) || string.IsNullOrWhiteSpace(a.Choice.Reason) ||
            a.Choice.ExpectedProgressPerAction.HasValue && !Finite(a.Choice.ExpectedProgressPerAction.Value,-1,1))
            throw new InvalidDataException("Invalid active strategy attempt");
    }
    private static bool Finite(double x,double min,double max)=>double.IsFinite(x)&&x>=min&&x<=max;
}
