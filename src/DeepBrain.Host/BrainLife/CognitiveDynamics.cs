using System.Text.Json;
using System.Text.Json.Serialization;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V4;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed record CognitiveObservation(string Scenario, HomeostasisDto Body, double Novelty, double Agency,
    double Threat, double Social, double LoopStrength, bool SocialPending, bool Sleeping = false);

/// <summary>Bounded working memory, learned immediate effects and explainable affect; separate from ML checkpoints.</summary>
public sealed partial class CognitiveDynamics
{
    public static readonly string[] EmotionIds = { "calm", "curiosity", "joy", "fear", "sadness", "frustration", "surprise", "relief", "affection", "boredom" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private readonly Action<string> _log;
    private CognitiveDynamicsConfig _config = CognitiveDynamicsConfig.Default;
    private State _state = new();
    private string? _path, _lastError;
    private DateTimeOffset? _savedAt;
    private long _lastAttempt;
    private bool _dirty, _sleeping, _seedOutcomes = true;
    private readonly HashSet<string> _seenOutcomes = new();
    private double? _priorThreat;
    private CognitiveObservation? _current;
    private string? _goal;
    private ActionExpectationDto? _expectation;
    private EmotionSignalDto[] _signals = Array.Empty<EmotionSignalDto>();
    private Dictionary<string, double> _biases = new();
    public CognitiveDynamics(Action<string>? log = null) => _log = log ?? (_ => { });

    public void Configure(CognitiveDynamicsConfig settings)
    {
        lock (_gate)
        {
            var config = settings.Normalize(); var path = System.IO.Path.GetFullPath(config.Path);
            if (_path != path || _config.Enable && !config.Enable)
            { Flush(); if (_dirty) throw new IOException("Cannot replace unsaved cognition state"); }
            if (config.Enable && _path != path)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("Cognition state too large");
                var loaded = File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json)
                    ?? throw new InvalidDataException("Empty cognition state") : new State();
                Validate(loaded); _state = loaded; _path = path; _dirty = false; _lastAttempt = loaded.Clock;
                _lastError = null; _priorThreat = null; _seedOutcomes = true; _seenOutcomes.Clear();
                _signals = loaded.Emotions.Select(p => new EmotionSignalDto(p.Key,p.Value,"Сохранённая интенсивность; обновится при бодрствовании")).ToArray();
                _expectation = null; _current = null; _goal = null;
                _savedAt = File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)) : null;
            }
            _config = config;
        }
    }

    public void Begin(CognitiveObservation observation, IReadOnlyList<WorldEventDto> newEvents, ExecutiveStateDto? executive)
    {
        lock (_gate)
        {
            _biases = new(); _expectation = null; _current = observation; _sleeping = observation.Sleeping;
            if (!_config.Enable) return;
            Validate(observation);
            if (_state.Scenario != observation.Scenario)
            {
                _state.Scenario = observation.Scenario; _state.Memory.Clear(); _priorThreat = null;
                _state.Success = _state.Failure = _state.Social = _state.Surprise = 0; _dirty = true;
            }
            IntegrateOutcomes(executive?.RecentOutcomes ?? Array.Empty<GoalOutcomeDto>());
            _goal = executive?.Plan?.GoalId;
            if (_sleeping) { Flush(); return; }
            _state.Clock++; _dirty = true;
            _state.Success *= .97; _state.Failure *= .98; _state.Social *= .97; _state.Surprise *= .8;
            _state.Memory.RemoveAll(x => _state.Clock - x.Clock > _config.WorkingMemoryTtlTicks);
            foreach (var ev in newEvents.Where(e => !e.Consumed && double.IsFinite(e.Salience) && e.Salience >= .15))
                Remember("event:" + ev.Type, "event", ev.Type + ": " + ev.Payload, Math.Clamp(ev.Salience, 0, 1));
            if (executive?.Plan is { } plan)
                Remember("goal:" + plan.PlanId, "goal", plan.GoalTitle + ": " + plan.SuccessCriterion, .7);
            var relief = _priorThreat.HasValue ? Math.Max(0, _priorThreat.Value - observation.Threat) * 4 : 0;
            _priorThreat = observation.Threat;
            var memoryNovelty = _state.Memory.Where(x => x.Key == "event:novelty_opportunity")
                .Select(x => x.Salience * (1 - (double)(_state.Clock - x.Clock) / _config.WorkingMemoryTtlTicks)).DefaultIfEmpty(0).Max();
            var safe = observation.Body.Safety >= .5 && observation.Body.Pain < .4;
            var targets = new (string Id, double Value, string Evidence)[]
            {
                ("calm", (1-observation.Threat)*(1-observation.Body.Arousal)*observation.Body.Safety, "Безопасность и невысокое возбуждение"),
                ("curiosity", safe ? (.65*observation.Novelty+.35*memoryNovelty)*observation.Body.Energy : 0, "Доступная новизна и недавние сигналы в рабочей памяти"),
                ("joy", _state.Success, "Недавно подтверждено достижение цели"),
                ("fear", Math.Max(observation.Threat, Math.Max(1-observation.Body.Safety, observation.Body.Pain)), "Оценка угрозы, безопасности и боли"),
                ("sadness", .65*_state.Failure*(1-observation.Body.Energy), "Неудача цели на фоне истощения"),
                ("frustration", .6*_state.Failure+.4*observation.LoopStrength, "Неудача цели или непродуктивное повторение"),
                ("surprise", _state.Surprise, "Наблюдаемый эффект отличался от изученного ожидания"),
                ("relief", relief, "Наблюдаемое снижение угрозы между шагами"),
                ("affection", .7*_state.Social+.3*(observation.SocialPending ? observation.Social : 0), "Реальный контакт или текущий сигнал общения"),
                ("boredom", safe ? (1-observation.Novelty)*observation.LoopStrength*(1-observation.Body.Fatigue) : 0, "Мало новизны и повторяющееся поведение в безопасных условиях")
            };
            _signals = targets.Select(t =>
            {
                var value = Math.Clamp(_state.Emotions[t.Id] + _config.EmotionResponse*(Math.Clamp(t.Value, 0, 1)-_state.Emotions[t.Id]), 0, 1);
                _state.Emotions[t.Id] = value; return new EmotionSignalDto(t.Id, value, t.Evidence);
            }).ToArray();
            TrimMemory(); MaybeFlush();
        }
    }

    public IReadOnlyList<GoalDto> BiasGoals(IReadOnlyList<GoalDto> goals)
    {
        lock (_gate)
        {
            if (!_config.Enable || _sleeping || _current is null) return goals;
            return goals.Select(g => g with { Urgency = Math.Clamp(g.Urgency + (g.Id switch
            {
                "regulate" => .08*_state.Emotions["fear"],
                "explore" when SafeToExplore(_current) => .08*Math.Max(_state.Emotions["curiosity"], _state.Emotions["boredom"]),
                "connect" when _current.SocialPending => .05*_state.Emotions["affection"],
                _ => 0
            }), 0, 1) }).ToArray();
        }
    }

    public void ApplyBias(IDictionary<string, double> legalScores, string? activeGoal)
    {
        lock (_gate)
        {
            _biases = new(); _goal = activeGoal;
            if (!_config.Enable || _sleeping || _current is null) return;
            var o = _current; var safe = SafeToExplore(o);
            foreach (var action in legalScores.Keys.ToArray())
            {
                if (!double.IsFinite(legalScores[action]) || ActionCatalog.IndexOf(action) < 0) continue;
                // Urgent body needs suppress exploratory and social influence, including learned models.
                if (!safe && action is "explore_signal" or "emit_message") continue;
                if (action == "emit_message" && !o.SocialPending) continue;
                var emotional = action switch
                {
                    "breathe_slow" => _state.Emotions["fear"] + .3*_state.Emotions["frustration"],
                    "rest_short" => _state.Emotions["sadness"] + o.Body.Fatigue*.3,
                    "explore_signal" when safe => _state.Emotions["curiosity"] + .5*_state.Emotions["boredom"],
                    "focus_widen" => .5*_state.Emotions["surprise"],
                    "emit_message" => _state.Emotions["affection"],
                    "reframe_negative" => .5*_state.Emotions["frustration"],
                    _ => 0
                };
                var learned = _state.Models.TryGetValue(Key(o.Scenario, action), out var model) && model.Samples>=4
                    ? Math.Clamp(Utility(model.Effect, activeGoal)/.05, -1, 1)*Confidence(model) : 0;
                var bias = Math.Clamp(_config.MaxActionBias*(.6*emotional+.4*learned), -_config.MaxActionBias, _config.MaxActionBias);
                legalScores[action] += bias; _biases[action] = bias;
            }
        }
    }

    public void Observe(ActionDto action, CognitiveObservation before, CognitiveObservation after, bool invalid,
        int handledSignals, ExecutiveStateDto? executive)
    {
        lock (_gate)
        {
            if (!_config.Enable || _sleeping) return;
            Validate(before); Validate(after); IntegrateOutcomes(executive?.RecentOutcomes ?? Array.Empty<GoalOutcomeDto>());
            if (invalid || before.Scenario != after.Scenario || ActionCatalog.IndexOf(action.Name) < 0 ||
                !double.IsFinite(action.Strength) || action.Strength < .05 || action.Strength > 1) return;
            var key = Key(before.Scenario, action.Name);
            var actual = Vector(after).Zip(Vector(before), (a,b) => a-b).ToArray();
            _state.Models.TryGetValue(key, out var model);
            var known = model is { Samples: >= 4 };
            var error = known ? model!.Effect.Zip(actual, (p,a) => Math.Abs(p*action.Strength-a)).Average() : (double?)null;
            _expectation = new ActionExpectationDto(action.Name, model?.Samples ?? 0, known ? Confidence(model!) : 0,
                known ? Utility(model!.Effect, _goal)*action.Strength : 0, error, known ? "checked" : "learning");
            if (known)
            {
                _state.Surprise = Math.Clamp(error!.Value/.05, 0, 1);
                model!.Error = .9*model.Error + .1*error.Value;
                if (_state.Surprise > .3) Remember("expectation:"+action.Name, "prediction", "Неожиданный эффект: "+action.Name, _state.Surprise);
            }
            if (model is null)
            {
                if (_state.Models.Count >= 128) _state.Models.Remove(_state.Models.MinBy(p => p.Value.Clock).Key);
                model = new EffectModel(); _state.Models[key] = model;
            }
            model.Samples = Math.Min(1_000_000_000, model.Samples+1); model.Clock = _state.Clock;
            var rate = 1.0/Math.Min(50, model.Samples);
            for (var i=0;i<7;i++) model.Effect[i] += rate*(Math.Clamp(actual[i]/action.Strength,-1,1)-model.Effect[i]);
            _state.Observations++; if (action.Name == "emit_message" && handledSignals > 0) _state.Social = 1;
            _dirty = true; MaybeFlush();
        }
    }

    public CognitiveDynamicsDto Snapshot()
    {
        lock (_gate) return new(_config.Enable, _state.Clock,
            _state.Emotions.MaxBy(p => p.Value).Key, _signals.ToArray(),
            _state.Memory.OrderByDescending(x => x.Salience).Select(x => new WorkingMemoryItemDto(x.Key,x.Kind,x.Text,x.Salience,_state.Clock-x.Clock)).ToArray(),
            _expectation, _state.Models.Count, _state.Observations, _state.GoalOutcomes,
            new Dictionary<string,double>(_biases), _sleeping, _lastError, _savedAt);
    }

    public IReadOnlyList<StrategyEffectHint> StrategyHints()
    {
        lock (_gate)
        {
            if (!_config.Enable || _current is null || _sleeping) return Array.Empty<StrategyEffectHint>();
            return (from goal in new[] { "restore_energy", "regulate", "explore", "focus" }
                from action in ActionCatalog.Actions
                let key = Key(_current.Scenario, action)
                where _state.Models.TryGetValue(key, out var model) && model.Samples >= 4
                let model = _state.Models[key]
                select new StrategyEffectHint(goal, action, Utility(model.Effect, goal), Confidence(model))).ToArray();
        }
    }

    public IReadOnlyList<LearnedActionEffect> LearnedEffects(string scenario)
    {
        lock (_gate)
        {
            if (!_config.Enable) return Array.Empty<LearnedActionEffect>();
            return ActionCatalog.Actions.Where(a => _state.Models.ContainsKey(Key(scenario,a))).Select(a =>
            {
                var m = _state.Models[Key(scenario,a)];
                return new LearnedActionEffect(a, (double[])m.Effect.Clone(), m.Samples, Confidence(m));
            }).ToArray();
        }
    }

    private void IntegrateOutcomes(IReadOnlyList<GoalOutcomeDto> outcomes)
    {
        if (_seedOutcomes) { foreach (var o in outcomes) _seenOutcomes.Add(o.PlanId); _seedOutcomes = false; return; }
        foreach (var o in outcomes.Reverse())
        {
            if (!_seenOutcomes.Add(o.PlanId)) continue;
            _state.Memory.RemoveAll(x => x.Key=="goal:"+o.PlanId);
            _state.GoalOutcomes++; _dirty = true;
            if (o.Status == "succeeded") _state.Success = 1;
            if (o.Status == "failed") _state.Failure = 1;
            Remember("result:"+o.PlanId, "result", o.GoalTitle+": "+o.Status+"; "+o.Evidence, .85);
        }
        if (_seenOutcomes.Count > 200) { _seenOutcomes.Clear(); foreach (var o in outcomes) _seenOutcomes.Add(o.PlanId); }
    }
    private void Remember(string key,string kind,string text,double salience)
    {
        _state.Memory.RemoveAll(x => x.Key==key);
        _state.Memory.Add(new MemoryItem(key,kind,text.Length>240?text[..240]:text,salience,_state.Clock)); TrimMemory();
    }
    private void TrimMemory()
    { _state.Memory = _state.Memory.OrderByDescending(x => x.Salience*Math.Max(0,1-(double)(_state.Clock-x.Clock)/_config.WorkingMemoryTtlTicks)).Take(_config.WorkingMemoryCapacity).ToList(); }
    private static bool SafeToExplore(CognitiveObservation o) => o.Body.Safety>=.5 && o.Body.Pain<.4 && o.Body.Energy>.35 && o.Body.Fatigue<.75;
    private static string Key(string scenario,string action) => scenario+"/"+action;
    private static double[] Vector(CognitiveObservation o) => new[] { o.Body.Energy,o.Body.Fatigue,o.Body.Safety,o.Body.Pain,o.Body.Arousal,o.Novelty,o.Agency };
    private static double Confidence(EffectModel m) => Math.Clamp(m.Samples/(m.Samples+12.0)*(1-m.Error/.06),0,.9);
    private static double Utility(double[] d,string? goal) => goal switch
    { "restore_energy" => d[0]-d[1], "regulate" => d[2]-d[3]-d[4], "explore" => -d[5], "focus" => -d[6], _ => d[0]+d[2]-d[3]-d[1] };
    private void MaybeFlush() { if (_state.Clock-_lastAttempt>=_config.SaveEveryTicks) Flush(); }
    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty || _path is null) return; _lastAttempt=_state.Clock;
            var temp=_path+".tmp-"+Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                { JsonSerializer.Serialize(file,_state,Json);file.Flush(true); }
                File.Move(temp,_path,true);_dirty=false;_lastError=null;_savedAt=DateTimeOffset.UtcNow;
            }
            catch(Exception e) { _lastError=e.Message;_log("Ошибка сохранения когнитивного состояния: "+e.Message);try { File.Delete(temp); } catch { } }
        }
    }
    private static void Validate(CognitiveObservation o)
    {
        if(string.IsNullOrWhiteSpace(o.Scenario) || Vector(o).Concat(new[]{o.Threat,o.Social,o.LoopStrength}).Any(v=>!double.IsFinite(v)||v<0||v>1))
            throw new InvalidDataException("Invalid cognitive observation");
    }
    private static void Validate(State s)
    {
        if(s.SchemaVersion!=1 || s.Clock<0 || s.Observations<0 || s.GoalOutcomes<0 || s.Scenario is null ||
            s.Emotions is null || s.Memory is null || s.Models is null || s.Memory.Count>32 || s.Models.Count>128 ||
            !EmotionIds.Order().SequenceEqual(s.Emotions.Keys.Order()) ||
            s.Emotions.Values.Concat(new[]{s.Success,s.Failure,s.Social,s.Surprise}).Any(v=>!double.IsFinite(v)||v<0||v>1) ||
            s.Memory.Any(x=>x is null || string.IsNullOrWhiteSpace(x.Key)||x.Text is null||x.Text.Length>240||x.Clock<0||x.Clock>s.Clock||!double.IsFinite(x.Salience)||x.Salience<0||x.Salience>1) ||
            s.Models.Any(x=>!ActionCatalog.Actions.Any(a=>x.Key.EndsWith("/"+a,StringComparison.Ordinal)) || x.Value is null ||
                x.Value.Samples<1||x.Value.Samples>1_000_000_000||x.Value.Clock<0||x.Value.Clock>s.Clock||
                !double.IsFinite(x.Value.Error)||x.Value.Error<0||x.Value.Error>1||x.Value.Effect is null||x.Value.Effect.Length!=7||x.Value.Effect.Any(v=>!double.IsFinite(v)||v< -1||v>1)))
            throw new InvalidDataException("Invalid persisted cognition state");
    }
    private sealed class State
    {
        [JsonRequired] public int SchemaVersion {get;set;}=1;
        [JsonRequired] public long Clock {get;set;}
        [JsonRequired] public string Scenario {get;set;}="";
        [JsonRequired] public long Observations {get;set;}
        [JsonRequired] public long GoalOutcomes {get;set;}
        [JsonRequired] public Dictionary<string,double> Emotions {get;set;}=EmotionIds.ToDictionary(x=>x,_=>0.0);
        [JsonRequired] public List<MemoryItem> Memory {get;set;}=new();
        [JsonRequired] public Dictionary<string,EffectModel> Models {get;set;}=new();
        [JsonRequired] public double Success {get;set;}
        [JsonRequired] public double Failure {get;set;}
        [JsonRequired] public double Social {get;set;}
        [JsonRequired] public double Surprise {get;set;}
    }
    private sealed record MemoryItem([property:JsonRequired]string Key,[property:JsonRequired]string Kind,
        [property:JsonRequired]string Text,[property:JsonRequired]double Salience,[property:JsonRequired]long Clock);
    private sealed class EffectModel
    {
        [JsonRequired] public long Samples {get;set;}
        [JsonRequired] public long Clock {get;set;}
        [JsonRequired] public double Error {get;set;}
        [JsonRequired] public double[] Effect {get;set;}=new double[7];
    }
}
