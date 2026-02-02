using System.Text.Json.Serialization;

namespace DeepBrain.Shared.Net;

public sealed record Envelope
{
    public string Type { get; init; }
    public string Id { get; init; }
    public long Ts { get; init; }
    public object? Payload { get; init; }

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public static string NewId() => Guid.NewGuid().ToString("N");

    [JsonConstructor]
    public Envelope(string type, string id, long ts, object? payload)
    {
        Type = type;
        Id = id;
        Ts = ts;
        Payload = payload;
    }

    // 3 args: type + id + payload
    public Envelope(string type, string id, object? payload)
        : this(type, id, NowMs(), payload) { }

    // 2 args: type + payload
    public Envelope(string type, object? payload)
        : this(type, NewId(), NowMs(), payload) { }

    // 2 args: type + id
    public Envelope(string type, string id)
        : this(type, id, NowMs(), null) { }
}
