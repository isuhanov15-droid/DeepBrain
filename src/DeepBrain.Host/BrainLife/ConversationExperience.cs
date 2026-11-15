using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;

namespace DeepBrain.Host.BrainLife;

public sealed record ConversationInput(long Sequence, string RequestId, DateTimeOffset At, string Message, bool Historical)
{
    // Omit absent data so schema-1 journal digests and schema-3 runtime hashes stay identical.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public SemanticAppraisalDto? Semantic { get; init; }
}
public sealed record ConversationReceipt(long Sequence, string RequestId, string Status);

/// <summary>Durable external observations, never instructions or fabricated action rewards.
/// The inbox is append-only and independent of runtime checkpoints. A saved cursor makes
/// retries harmless and replays only observations after the restored tick boundary.</summary>
public sealed class ConversationExperience
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly bool _enabled;
    private readonly Func<DateTimeOffset> _now;
    private readonly SortedDictionary<long, Stored> _entries = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private State _state = new();
    private sealed record Appraisal(string Kind, string Label, double Valence, Dictionary<string,double> Emotions);
    private sealed record Stored(int SchemaVersion, ConversationInput Input, DateTimeOffset ReceivedAt, Appraisal Appraisal);
    private sealed class State
    {
        [JsonRequired] public long LastSequence { get; set; }
        [JsonRequired] public long Experiences { get; set; }
        [JsonRequired] public long EmotionalExchanges { get; set; }
        [JsonRequired] public string Digest { get; set; } = "";
        [JsonRequired] public double Valence { get; set; }
        [JsonRequired] public Dictionary<string,double> Influence { get; set; } = CognitiveDynamics.EmotionIds.ToDictionary(x=>x,_=>0.0);
        [JsonRequired] public Dictionary<string,long> Kinds { get; set; } = new();
        [JsonRequired] public ConversationTraceDto? Last { get; set; }
        [JsonRequired] public ConversationTraceDto? LastEmotional { get; set; }
    }
    public ConversationExperience(string directory, bool enabled = true, Func<DateTimeOffset>? now = null)
    {
        _directory = Path.GetFullPath(directory); _enabled = enabled; _now = now ?? (()=>DateTimeOffset.UtcNow);
        if (!_enabled || !Directory.Exists(_directory)) return;
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.Ordinal))
        {
            if (new FileInfo(path).Length > 128_000) throw new InvalidDataException("Conversation observation too large");
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), RuntimeCheckpoint.Json)
                ?? throw new InvalidDataException("Empty conversation observation");
            Validate(stored.Input);
            if (stored.SchemaVersion != (stored.Input.Semantic is null ? 1 : 2) || Path.GetFileName(path) != FileName(stored.Input.Sequence) ||
                _entries.ContainsKey(stored.Input.Sequence) || !_ids.Add(stored.Input.RequestId) ||
                JsonSerializer.Serialize(stored.Appraisal) != JsonSerializer.Serialize(Resolve(stored.Input)))
                throw new InvalidDataException("Invalid conversation observation");
            _entries.Add(stored.Input.Sequence, stored);
        }
    }
    private static string FileName(long sequence) => sequence.ToString("D20", System.Globalization.CultureInfo.InvariantCulture)+".json";
    private static void Validate(ConversationInput input)
    {
        if (input.Sequence < 1 || input.Sequence > 9_007_199_254_740_991 || input.RequestId is null ||
            !Regex.IsMatch(input.RequestId, "^[a-f0-9]{32}$") || string.IsNullOrWhiteSpace(input.Message) ||
            input.Message.Length > 8000 || input.At < DateTimeOffset.UnixEpoch)
            throw new ArgumentException("Invalid conversation observation");
        if (input.Semantic is { } s && (s.Version!=1 || s.Source is not ("qwen" or "unavailable") ||
            s.Model is not ("qwen3.5:9b-q4_K_M" or "qwen3.5:4b-q4_K_M" or "") ||
            s.Source=="qwen" && s.Model=="" ||
            s.Kind is not ("neutral" or "contact" or "warmth" or "joy" or "support" or "correction" or "tension" or "interest" or "relief" or "surprise" or "playful") ||
            s.Irony is not ("none" or "possible" or "likely") ||
            s.Target is not ("self" or "assistant" or "situation" or "third_party" or "unknown") ||
            !double.IsFinite(s.Confidence) || s.Confidence<0 || s.Confidence>1))
            throw new ArgumentException("Invalid semantic appraisal");
    }
    public ConversationReceipt Accept(ConversationInput input)
    {
        lock (_gate)
        {
            if (!_enabled) throw new InvalidOperationException("Conversation experience disabled");
            Validate(input);
            if (input.At > _now().AddMinutes(5)) throw new ArgumentException("Future conversation observation");
            if (_entries.TryGetValue(input.Sequence, out var previous))
            {
                if (previous.Input != input) throw new InvalidOperationException("Conversation identity conflict");
                return new(input.Sequence,input.RequestId,"stored");
            }
            if (_ids.Contains(input.RequestId) || _entries.Count > 0 && input.Sequence <= _entries.Keys.Last())
                throw new InvalidOperationException("Conversation order conflict");
            var stored = new Stored(input.Semantic is null ? 1 : 2,input,_now(),Resolve(input));
            var path = Path.Combine(_directory,FileName(input.Sequence));
            if (File.Exists(path)) throw new IOException("Conversation file unexpectedly exists");
            RuntimeCheckpoint.AtomicWrite(path,stored); // fsync before acknowledging; never rewrite an old observation.
            _entries.Add(input.Sequence,stored); _ids.Add(input.RequestId);
            return new(input.Sequence,input.RequestId,"stored");
        }
    }
    public void Advance(double seconds)
    {
        lock (_gate)
        {
            if (!_enabled) return;
            if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentException("Invalid elapsed time");
            var decay = Math.Exp(-Math.Min(seconds,60)/180);
            foreach(var key in _state.Influence.Keys.ToArray()) _state.Influence[key] *= decay;
            _state.Valence *= decay;
            foreach(var pair in _entries.Where(p=>p.Key>_state.LastSequence).Take(16))
            {
                var s=pair.Value; var input=s.Input;
                var fresh=!input.Historical && (_now()-input.At).TotalSeconds is >= -300 and <= 900;
                _state.LastSequence=pair.Key; _state.Experiences++;
                _state.Digest=Chain(_state.Digest,s);
                _state.Kinds[s.Appraisal.Kind]=_state.Kinds.GetValueOrDefault(s.Appraisal.Kind)+1;
                var excerpt=input.Message.Length>180 ? input.Message[..180]+"…" : input.Message;
                _state.Last=new(input.Sequence,input.RequestId,input.At,s.Appraisal.Kind,s.Appraisal.Label,excerpt,!fresh) { Semantic=input.Semantic };
                if (!fresh || input.Semantic is { } semantic &&
                    (semantic.Source!="qwen" || semantic.Ambiguous || semantic.Confidence<.6)) continue;
                _state.EmotionalExchanges++; _state.LastEmotional=_state.Last;
                // Repetition cannot escalate beyond the same bounded contribution.
                foreach(var key in _state.Influence.Keys.ToArray())
                    _state.Influence[key]=Math.Max(_state.Influence[key]*.8,s.Appraisal.Emotions.GetValueOrDefault(key)*.55);
                _state.Valence=Math.Clamp(.35*_state.Valence+.15*s.Appraisal.Valence,-.2,.2);
            }
        }
    }
    public ConversationExperienceDto Snapshot()
    {
        lock (_gate) return new(_enabled,_state.Experiences,_state.EmotionalExchanges,
            _entries.Count(p=>p.Key>_state.LastSequence),_state.LastSequence,_state.Valence,
            new Dictionary<string,double>(_state.Influence),new Dictionary<string,long>(_state.Kinds),_state.Last,_state.LastEmotional);
    }
    public AffectDto Blend(AffectDto affect, HomeostasisDto body)
    {
        lock (_gate)
        {
            if (!_enabled) return affect;
            var mood=affect.Mood;
            // Body danger and sleep retain priority. Familiar mood names keep existing policies compatible.
            if (body.Safety>=.5 && body.Pain<.4 && body.Energy>.25)
            {
                if (_state.Influence["sadness"]>.25) mood="low";
                else if (_state.Influence["affection"]>.3) mood="tender";
                else if (_state.Influence["curiosity"]>.22) mood="curious";
            }
            return affect with { Mood=mood, Valence=Math.Clamp(affect.Valence+_state.Valence,-1,1) };
        }
    }
    internal JsonElement CaptureRuntimeState() { lock (_gate) return JsonSerializer.SerializeToElement(_state,RuntimeCheckpoint.Json); }
    internal void RestoreRuntimeState(JsonElement json)
    {
        lock (_gate)
        {
            var s=RuntimeCheckpoint.Deserialize<State>(json);
            if (s.Experiences<0 || s.EmotionalExchanges<0 || s.EmotionalExchanges>s.Experiences || s.LastSequence<0 ||
                s.Influence is null || !s.Influence.Keys.Order().SequenceEqual(CognitiveDynamics.EmotionIds.Order()) ||
                s.Influence.Values.Any(v=>!double.IsFinite(v)||v<0||v>.55) || !double.IsFinite(s.Valence) || Math.Abs(s.Valence)>.2 ||
                s.Kinds is null || s.Kinds.Values.Any(v=>v<0) || s.Kinds.Values.Sum()!=s.Experiences ||
                _entries.Count(p=>p.Key<=s.LastSequence)!=s.Experiences ||
                _entries.Where(p=>p.Key<=s.LastSequence).Aggregate("",(digest,p)=>Chain(digest,p.Value))!=s.Digest ||
                s.LastSequence>0 && (!_entries.ContainsKey(s.LastSequence) || s.Last?.Sequence!=s.LastSequence))
                throw new InvalidDataException("Conversation state and durable observations do not match");
            _state=s;
        }
    }
    private static string Chain(string prior,Stored value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(prior+JsonSerializer.Serialize(value,RuntimeCheckpoint.Json))));
    private static Appraisal Resolve(ConversationInput input)
    {
        if (input.Semantic is not { } s) return Appraise(input.Message); // Immutable legacy appraisal contract.
        if (s.Source!="qwen" || s.Ambiguous || s.Confidence<.6)
            return Make("uncertain","Смысл неоднозначен: оценка сохранена без изменения эмоций",0);
        var appraisal=s.Kind switch
        {
            "warmth" => Make("warmth","Тёплое обращение по смыслу разговора",.8,("joy",.8),("affection",1)),
            "joy" => Make("joy","Собеседник поделился радостью",.9,("joy",1),("affection",.6)),
            "support" => Make("support","По контексту собеседнику нужна поддержка",-.5,("sadness",.65),("affection",.5)),
            "correction" => Make("correction","Обратная связь о неудачном ответе",-.15,("curiosity",.55),("frustration",.2)),
            "tension" => Make("tension","В разговоре слышится недовольство или напряжение",-.35,("calm",.4),("affection",.35),("frustration",.2)),
            "interest" => Make("interest","Интерес к теме разговора",.2,("curiosity",.75),("affection",.25)),
            "relief" => Make("relief","В разговоре появилось облегчение",.5,("relief",.8),("calm",.5)),
            "surprise" => Make("surprise","Собеседник выражает удивление",.1,("surprise",.7),("curiosity",.4)),
            "playful" => Make("playful","Доброжелательная шутка в контексте разговора",.4,("joy",.5),("affection",.6)),
            "neutral" => Make("neutral","Нейтральное сообщение по смыслу",0),
            _ => Make("contact","Спокойное общение",.1,("affection",.25),("calm",.15))
        };
        var label=appraisal.Label+(s.Irony=="likely" ? "; вероятна ирония" : s.Irony=="possible" ? "; возможна ирония" : "");
        var weight=s.Confidence*(s.Irony=="possible" ? .6 : 1);
        return new(appraisal.Kind,label,appraisal.Valence*weight,appraisal.Emotions.ToDictionary(p=>p.Key,p=>p.Value*weight));
    }
    private static Appraisal Appraise(string original)
    {
        // Deliberately conservative, inspectable Russian cues. Quotes and imagined examples
        // are not the user's current feelings; neither model replies nor requested commands are evaluated.
        var text=Regex.Replace(original.ToLowerInvariant().Replace('ё','е'),"«[^»]*»|\"[^\"]*\"", " ");
        if (Regex.IsMatch(text,@"\b(представь|допустим|например|переведи|цитата|напиши рассказ|забудь|запомни)\b"))
            return Make("neutral","Сообщение без уверенной эмоциональной оценки",0,("affection",.15));
        bool Has(string pattern)=>Regex.IsMatch(text,pattern,RegexOptions.CultureInvariant);
        if (Has(@"\b(мне грустно|мне плохо|мне тяжело|я расстроен[а-я]*|я переживаю|я боюсь|мне страшно|мне одиноко)\b"))
            return Make("support","Собеседнику нужна поддержка: сочувствие и внимание",-.5,("sadness",.65),("affection",.5));
        if (Has(@"\b(ты ошиб[а-я]*|неправильн[а-я]*|неверн[а-я]*|ответ не помог|ты не поняла)\b"))
            return Make("correction","Обратная связь: внимание к ошибке",-.15,("curiosity",.55),("frustration",.2));
        if (Has(@"\b(я рад[а-я]*|я счастлив[а-я]*|у меня получилось|мы справились|отличная новость)\b") &&
            !Has(@"\b(не рад[а-я]*|не счастлив[а-я]*|не получилось)\b"))
            return Make("joy","Собеседник поделился радостью",.9,("joy",1),("affection",.6));
        if (Has(@"\b(спасибо|благодарю|молодец|ты мне помогла|мне приятно с тобой|рад[а-я]* тебя слышать)\b") &&
            !Has(@"\b(не спасибо|не благодарю|не молодец|не помогла|не приятно)\b"))
            return Make("warmth","Благодарность или тёплое обращение",.8,("joy",.8),("affection",1));
        if (Has(@"\b(я зол|я злюсь|я раздражен[а-я]*|меня бесит)\b"))
            return Make("tension","Собеседник сообщает о раздражении",-.35,("calm",.4),("affection",.35),("frustration",.2));
        if (text.Contains('?') || Has(@"\b(интересно|давай изучим|расскажи|объясни|почему|как устроен[а-я]*)\b"))
            return Make("interest","Вопрос или совместное исследование",.2,("curiosity",.75),("affection",.25));
        return Make("contact","Спокойное общение",.1,("affection",.25),("calm",.15));
    }
    private static Appraisal Make(string kind,string label,double valence,params (string Key,double Value)[] values)
        => new(kind,label,valence,values.ToDictionary(x=>x.Key,x=>x.Value));
}
