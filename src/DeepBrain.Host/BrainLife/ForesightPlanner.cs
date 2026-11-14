using System.Text.Json;
using System.Text.Json.Serialization;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed record ForesightConfig(bool Enable = false, int Horizon = 3, int BeamWidth = 4,
    double MaxActionBias = .12, double MinConfidence = .35)
{
    public void Validate()
    {
        if (Horizon is < 1 or > 3 || BeamWidth is < 1 or > 8 || !double.IsFinite(MaxActionBias) ||
            MaxActionBias is < 0 or > .2 || !double.IsFinite(MinConfidence) || MinConfidence is < .2 or > .9)
            throw new InvalidDataException("Invalid foresight configuration");
    }
}
public sealed record LearnedActionEffect(string Action, double[] Effect, long Samples, double Confidence);
public sealed record ForecastObservation(string Scenario, int EpisodeId, string? PlanId, string? GoalId,
    HomeostasisDto Body, AffectDto Affect, double Novelty, double Agency, bool SocialPending, long Tick)
{
    public ForecastStateDto State => new(Body, Novelty, Agency, Affect.Valence, Affect.Arousal, SocialPending);
}

// Conditional, bounded look-ahead. Imagined states never reach actuators, memories,
// the cognitive learner or ML replay. Only Observe receives real outcomes.
public sealed class ForesightPlanner
{
    // Future drive estimates are approximate. Small intensity changes remain
    // part of the measured prediction error; a material change retires the path.
    private const double FutureStrengthTolerance = .05;
    private ForesightConfig _config = new();
    private Store _state = new();
    public bool Enabled => _config.Enable;
    public void Configure(ForesightConfig? config) { _config = config ?? new(); _config.Validate(); }

    private sealed class Model
    {
        [JsonRequired] public long Samples { get; set; }
        [JsonRequired] public double[] Effect { get; set; } = new double[9];
        [JsonRequired] public double[] Center { get; set; } = new double[9];
        [JsonRequired] public double Error { get; set; } = .02;
        [JsonRequired] public long Updated { get; set; }
    }
    private sealed class Pending
    {
        [JsonRequired] public string Id { get; set; } = "";
        [JsonRequired] public ForecastObservation Start { get; set; } = null!;
        [JsonRequired] public ForecastOptionDto Option { get; set; } = null!;
        [JsonRequired] public int Checked { get; set; }
        [JsonRequired] public double ErrorSum { get; set; }
        [JsonRequired] public ForecastStateDto LastActual { get; set; } = null!;
    }
    private sealed class Store
    {
        [JsonRequired] public int SchemaVersion { get; set; } = 1;
        [JsonRequired] public Dictionary<string, Model> Models { get; set; } = new(StringComparer.Ordinal);
        [JsonRequired] public Dictionary<string, Model> Drift { get; set; } = new(StringComparer.Ordinal);
        [JsonRequired] public ForecastObservation? LastAfter { get; set; }
        [JsonRequired] public Pending? Pending { get; set; }
        [JsonRequired] public List<ForecastCheckDto> History { get; set; } = new();
        [JsonRequired] public ForecastOptionDto[] Options { get; set; } = Array.Empty<ForecastOptionDto>();
        [JsonRequired] public string Status { get; set; } = "learning";
        [JsonRequired] public string? Recommended { get; set; }
        [JsonRequired] public string? Selected { get; set; }
        [JsonRequired] public double AppliedBias { get; set; }
        [JsonRequired] public long Serial { get; set; }
        [JsonRequired] public long Predictions { get; set; }
        [JsonRequired] public long CheckedSteps { get; set; }
        [JsonRequired] public long Completed { get; set; }
        [JsonRequired] public double ErrorSum { get; set; }
    }
    private sealed record Node(ForecastStateDto State, ActionCooldowns Cooldowns,
        ForecastStepDto[] Steps, double Confidence, double Uncertainty, double Cost, double Risk);
    private sealed record Estimate(double[] Effect, double Confidence, double Error, string Source);
    private static double[] Vector(ForecastStateDto s) => new[] { s.Body.Energy, s.Body.Fatigue, s.Body.Safety,
        s.Body.Pain, s.Body.Arousal, s.Novelty, s.Agency, s.Valence, s.Arousal };
    private static double Error(ForecastStateDto a, ForecastStateDto b) => Vector(a).Zip(Vector(b), (x,y) => Math.Abs(x-y)).Average();
    private static string Context(ForecastStateDto s) => s.Body.Energy < .35 || s.Body.Fatigue > .75 ? "depleted" :
        s.Body.Safety < .5 || s.Body.Pain > .4 || s.Body.Arousal > .5 ? "strained" : "steady";
    private static string Key(string scenario, ForecastStateDto s) => scenario + "/" + Context(s);
    private static bool SameContext(ForecastObservation a, ForecastObservation b) =>
        a.Scenario == b.Scenario && a.EpisodeId == b.EpisodeId && a.PlanId == b.PlanId && a.GoalId == b.GoalId;
    private static double Confidence(Model m, ForecastStateDto s)
    {
        var distance = Vector(s).Zip(m.Center, (a,b) => Math.Abs(a-b)).Average();
        return Math.Clamp(m.Samples / (m.Samples + 8.0) * (1-m.Error/.08) * (1-distance/.5), 0, .9);
    }
    private Estimate? Effect(ForecastObservation o, ForecastStateDto s, string action, IReadOnlyList<LearnedActionEffect> priors)
    {
        if (_state.Models.TryGetValue(Key(o.Scenario,s)+"/"+action, out var m) && m.Samples >= 4)
            return new(m.Effect, Confidence(m,s), m.Error, "measured");
        var prior = priors.FirstOrDefault(p => p.Action==action && p.Samples>=4 && p.Effect.Length==7 &&
            p.Effect.All(double.IsFinite) && double.IsFinite(p.Confidence));
        return prior is null ? null : new(prior.Effect.Concat(new double[2]).ToArray(), Math.Clamp(prior.Confidence*.5,0,.4), .03, "prior_effects");
    }
    private static ForecastStateDto Add(ForecastStateDto s, double[] effect, double strength, string? action = null)
    {
        var v=Vector(s).Zip(effect,(x,d)=>x+d*strength).Select((x,i)=>Math.Clamp(x,i==7?-1:0,1)).ToArray();
        return new(new(v[0],v[1],v[4],v[3],v[2]),v[5],v[6],v[7],v[8],action=="emit_message"?false:s.SocialPending);
    }
    private static double Risk(ForecastStateDto s, double uncertainty) =>
        Math.Max(0,.35-(s.Body.Safety-uncertainty)) + Math.Max(0,s.Body.Pain+uncertainty-.65) +
        Math.Max(0,.18-(s.Body.Energy-uncertainty)) + Math.Max(0,s.Body.Fatigue+uncertainty-.85);
    private static double Progress(string? goal, ForecastStateDto before, ForecastStateDto after)
    {
        double Distance(ForecastStateDto s) => goal switch
        {
            "restore_energy" => Math.Max(0,.75-s.Body.Energy)+Math.Max(0,s.Body.Fatigue-.25),
            "regulate" => Math.Max(0,.70-s.Body.Safety)+Math.Max(0,s.Body.Pain-.20)+Math.Max(0,s.Body.Arousal-.35),
            "focus" => s.Agency, _ => 0
        };
        return goal switch { "explore" => before.Novelty-after.Novelty,
            "connect" => before.SocialPending && !after.SocialPending ? 1 : 0,
            null => (after.Body.Energy-before.Body.Energy)+(after.Body.Safety-before.Body.Safety),
            _ => Distance(before)-Distance(after) };
    }
    private static ActionCooldowns Copy(ActionCooldowns source)
    { var copy=new ActionCooldowns();foreach(var action in ActionCatalog.Actions) { var tick=source.GetLastTick(action);if(tick>=0)copy.Mark(action,tick); }return copy; }

    public void Plan(ForecastObservation observation, IReadOnlyList<ActionDto> actions, IDictionary<string,double> scores,
        ActionCooldowns cooldowns, ActionsConfig limits, int emitCooldown, bool allowLoopBreak, double panicThreshold,
        IReadOnlyList<LearnedActionEffect> priors, int horizonLimit = 3)
    {
        if (!Enabled) return;
        ValidateObservation(observation);
        if (_state.LastAfter is { } previous && previous.Scenario==observation.Scenario &&
            previous.EpisodeId==observation.EpisodeId && previous.Tick+1==observation.Tick)
            Learn(_state.Drift,Key(observation.Scenario,previous.State),previous.State,observation.State,1,observation.Tick,24);
        _state.LastAfter=null;
        if (_state.Pending is { } pending && (!SameContext(pending.Start,observation) ||
            pending.Start.Tick+pending.Checked!=observation.Tick)) Finish("context_changed");
        _state.AppliedBias=0;_state.Selected=null;_state.Recommended=null;
        var pool=actions.Where(a=>ActionCatalog.IndexOf(a.Name)>=0 && double.IsFinite(a.Strength) && a.Strength is >=.05 and <=1)
            .DistinctBy(a=>a.Name).Take(ActionCatalog.Count).ToArray();
        var available=pool.Where(a=>scores.TryGetValue(a.Name,out var score)&&double.IsFinite(score)).ToArray();
        var urgent=observation.Body.Safety<.35 || observation.Body.Pain>.65 || observation.Body.Energy<.25 || observation.Body.Fatigue>.85;
        var horizon=Math.Min(_config.Horizon,Math.Clamp(horizonLimit,1,3));
        var options=new List<ForecastOptionDto>();
        foreach(var first in available)
        {
            var beam=new List<Node>{new(observation.State,Copy(cooldowns),Array.Empty<ForecastStepDto>(),1,0,0,0)};
            for(var depth=0;depth<horizon;depth++)
            {
                var expanded=new List<Node>();
                foreach(var node in beam)
                {
                    var start=node.State;var confidence=node.Confidence;var uncertainty=node.Uncertainty;
                    if(depth>0)
                    {
                        if(!_state.Drift.TryGetValue(Key(observation.Scenario,start),out var drift)||drift.Samples<8) continue;
                        confidence=Math.Min(confidence,Confidence(drift,start));
                        if(confidence<_config.MinConfidence)continue;
                        start=Add(start,drift.Effect,1);uncertainty+=drift.Error;
                    }
                    var mask=new ActionMasker().BuildMask(start.Body,new("forecast",start.Valence,start.Arousal),node.Cooldowns,
                        observation.Tick+depth,emitCooldown,limits,allowLoopBreak,panicThreshold);
                    foreach(var action in depth==0?new[]{first}:pool)
                    {
                        if(mask[ActionCatalog.IndexOf(action.Name)]<=0 || action.Name=="emit_message"&&!start.SocialPending)continue;
                        var effect=Effect(observation,start,action.Name,priors);if(effect is null)continue;
                        var c=Math.Min(confidence,effect.Confidence)*Math.Pow(.95,depth);
                        if(depth>0&&c<_config.MinConfidence)continue;
                        var after=Add(start,effect.Effect,action.Strength,action.Name);
                        var u=uncertainty+effect.Error+.005*(1-c);
                        var risk=Math.Max(node.Risk,Risk(after,2*u));
                        if(depth>0 && risk>0)continue;
                        var copy=Copy(node.Cooldowns);copy.Mark(action.Name,observation.Tick+depth);
                        expanded.Add(new(after,copy,node.Steps.Append(new(action.Name,action.Strength,after,c,u,effect.Source)).ToArray(),
                            c,u,node.Cost+Math.Max(0,node.State.Body.Energy-start.Body.Energy)+Math.Max(0,start.Body.Energy-after.Body.Energy),risk));
                    }
                }
                if(expanded.Count==0)break;
                beam=expanded.OrderByDescending(n=>Score(observation,n)).ThenBy(n=>string.Join("/",n.Steps.Select(x=>x.Action)),StringComparer.Ordinal)
                    .Take(_config.BeamWidth).ToList();
            }
            var best=beam.Where(n=>n.Steps.Length>0).OrderByDescending(n=>Score(observation,n)).FirstOrDefault();
            options.Add(best is null ? new(first.Name,Array.Empty<ForecastStepDto>(),0,0,0,0,0,"unknown") :
                new(first.Name,best.Steps,Progress(observation.GoalId,observation.State,best.State),best.Cost,best.Risk,best.Confidence,
                    Score(observation,best),best.Risk>0?"risk":best.Confidence<_config.MinConfidence?"uncertain":"evaluated"));
        }
        _state.Options=options.OrderByDescending(o=>o.Score).ThenBy(o=>o.FirstAction,StringComparer.Ordinal).ToArray();
        _state.Predictions++;
        var safe=options.Where(o=>o.Status=="evaluated").OrderByDescending(o=>o.Score).ThenBy(o=>o.FirstAction,StringComparer.Ordinal).ToArray();
        _state.Status=urgent?"urgent_recovery":safe.Length==0?"learning":horizon<2?"short_horizon":"compared";
        if(urgent||safe.Length==0)return;
        _state.Recommended=safe[0].FirstAction;
        // A small bounded vote; never opens a masked action or bypasses ML/executive safety.
        var spread=safe[0].Score-(safe.Length>1?safe[^1].Score:0);
        var bias=_config.MaxActionBias*safe[0].Confidence*Math.Clamp(spread/.06,0,1);
        if(bias>0) { scores[safe[0].FirstAction]+=bias;_state.AppliedBias=bias; }
    }
    private static double Score(ForecastObservation o, Node n)
    {
        // Earlier progress matters: terminal-only utility can keep postponing a
        // useful action to the end of each newly generated horizon forever.
        var previous=o.State;double progress=0;
        for(var i=0;i<n.Steps.Length;i++)
        { progress+=Math.Pow(.9,i)*Progress(o.GoalId,previous,n.Steps[i].Expected);previous=n.Steps[i].Expected; }
        return progress*n.Confidence - .25*n.Cost - 2*n.Risk - .3*n.Uncertainty;
    }

    public void Observe(ActionDto action, ForecastObservation before, ForecastObservation after, bool invalid)
    {
        if(!Enabled)return;
        ValidateObservation(before);ValidateObservation(after);_state.Selected=action.Name;
        if(invalid || before.Scenario!=after.Scenario || before.EpisodeId!=after.EpisodeId ||
            ActionCatalog.IndexOf(action.Name)<0 || !double.IsFinite(action.Strength) || action.Strength is <.05 or >1)
        { Finish("invalid");_state.LastAfter=null;return; }
        if(_state.Pending is { } pending && (pending.Checked>=pending.Option.Steps.Count ||
            pending.Option.Steps[pending.Checked].Action!=action.Name ||
            Math.Abs(pending.Option.Steps[pending.Checked].Strength-action.Strength)>FutureStrengthTolerance)) Finish("replanned");
        if(_state.Pending is null)
        {
            var option=_state.Options.FirstOrDefault(o=>o.FirstAction==action.Name && o.Steps.Count>0 &&
                Math.Abs(o.Steps[0].Strength-action.Strength)<1e-9);
            if(option is not null) _state.Pending=new(){Id="forecast-"+(++_state.Serial),Start=before,Option=option,LastActual=before.State};
        }
        if(_state.Pending is { } active)
        {
            var error=Error(active.Option.Steps[active.Checked].Expected,after.State);
            active.ErrorSum+=error;active.Checked++;active.LastActual=after.State;
            _state.CheckedSteps++;_state.ErrorSum+=error;
            if(active.Checked==active.Option.Steps.Count)Finish("checked");
            else if(error>.08)Finish("prediction_mismatch");
        }
        Learn(_state.Models,Key(before.Scenario,before.State)+"/"+action.Name,before.State,after.State,action.Strength,before.Tick,192);
        _state.LastAfter=after;
    }
    private static void Learn(Dictionary<string,Model> models,string key,ForecastStateDto before,ForecastStateDto after,double strength,long tick,int capacity)
    {
        if(!models.TryGetValue(key,out var model))
        {
            if(models.Count>=capacity)models.Remove(models.MinBy(x=>x.Value.Updated).Key);
            models[key]=model=new();
        }
        var initial=Vector(before);var actual=Vector(after).Zip(initial,(a,b)=>a-b).ToArray();
        if(model.Samples>0)model.Error=.9*model.Error+.1*model.Effect.Zip(actual,(p,a)=>Math.Abs(p*strength-a)).Average();
        model.Samples=Math.Min(1_000_000_000,model.Samples+1);model.Updated=tick;var rate=1.0/Math.Min(32,model.Samples);
        for(var i=0;i<9;i++){model.Effect[i]+=rate*(Math.Clamp(actual[i]/strength,-1,1)-model.Effect[i]);model.Center[i]+=rate*(initial[i]-model.Center[i]);}
    }
    public void Suspend(string reason)
    { if(!Enabled)return;Finish(reason);_state.LastAfter=null;_state.Options=Array.Empty<ForecastOptionDto>();_state.Recommended=null;_state.Selected=null;_state.AppliedBias=0;_state.Status=reason; }
    private ForecastCheckDto Check(Pending p,string status) => new(p.Id,p.Start.GoalId,p.Start.Scenario,p.Start.Tick,status,
        p.Option.Steps.Count,p.Checked,p.Option.ExpectedProgress,Progress(p.Start.GoalId,p.Start.State,p.LastActual),
        p.Checked>0?p.ErrorSum/p.Checked:null,p.Option.FirstAction,p.Option.Confidence);
    private void Finish(string status)
    {
        if(_state.Pending is not { } p)return;
        _state.History.Add(Check(p,status));if(_state.History.Count>30)_state.History.RemoveAt(0);
        if(status=="checked"&&p.Option.Steps.Count>1)_state.Completed++;
        _state.Pending=null;
    }
    public ForesightDto Snapshot() => new(Enabled,_state.Status,_state.Recommended,_state.Selected,_state.AppliedBias,
        _state.Options,_state.Pending is { } p?Check(p,"checking"):null,_state.History.AsEnumerable().Reverse().ToArray(),
        _state.Predictions,_state.CheckedSteps,_state.Completed,_state.Models.Count,_state.CheckedSteps>0?_state.ErrorSum/_state.CheckedSteps:null);

    internal JsonElement CaptureRuntimeState() => JsonSerializer.SerializeToElement(_state,RuntimeCheckpoint.Json);
    internal void RestoreRuntimeState(JsonElement json)
    {
        var state=RuntimeCheckpoint.Deserialize<Store>(json);
        if(state.SchemaVersion!=1 || state.Models is null || state.Drift is null || state.History is null || state.Options is null ||
            state.Models.Count>192 || state.Drift.Count>24 || state.History.Count>30 || state.Options.Length>ActionCatalog.Count ||
            state.Serial<0 || state.Predictions<0 || state.CheckedSteps<0 || state.Completed<0 || state.ErrorSum<0)
            throw new InvalidDataException("Invalid foresight checkpoint");
        foreach(var m in state.Models.Values.Concat(state.Drift.Values))
            if(m is null || m.Samples<1 || m.Updated<0 || m.Effect is null || m.Center is null || m.Effect.Length!=9 || m.Center.Length!=9 ||
                m.Effect.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1)||m.Center.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1)||m.Error<0||m.Error>2)
                throw new InvalidDataException("Invalid learned forecast model");
        foreach(var option in state.Options.Concat(state.Pending is { } p?new[]{p.Option}:Array.Empty<ForecastOptionDto>()))
        {
            if(option is null || option.Steps is null || option.Steps.Count>3 || ActionCatalog.IndexOf(option.FirstAction)<0 ||
                option.Confidence is <0 or >1)throw new InvalidDataException("Invalid forecast path");
            foreach(var step in option.Steps)
            { if(step is null || ActionCatalog.IndexOf(step.Action)<0 || step.Strength is <.05 or >1)throw new InvalidDataException("Invalid forecast action");ValidateState(step.Expected); }
        }
        if(state.Pending is { } pending)
        {
            ValidateObservation(pending.Start);ValidateState(pending.LastActual);
            if(pending.Checked<1 || pending.Checked>=pending.Option.Steps.Count || pending.ErrorSum<0)throw new InvalidDataException("Invalid pending forecast");
        }
        if(state.LastAfter is { } last)ValidateObservation(last);
        _state=state;
    }
    private static void ValidateObservation(ForecastObservation o)
    {
        if(o is null || string.IsNullOrWhiteSpace(o.Scenario) || o.EpisodeId<0 || o.Tick<0)throw new InvalidDataException("Invalid forecast observation");
        ValidateState(o.State);
    }
    private static void ValidateState(ForecastStateDto s)
    {
        if(s is null || s.Body is null || Vector(s).Where((v,i)=>!double.IsFinite(v)||v<(i==7?-1:0)||v>1).Any())
            throw new InvalidDataException("Invalid forecast state");
    }
}
