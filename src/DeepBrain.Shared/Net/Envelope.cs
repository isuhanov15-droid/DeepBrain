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

    // Три аргумента: тип + идентификатор + данные.
    public Envelope(string type, string id, object? payload)
        : this(type, id, NowMs(), payload) { }

    // Два аргумента: тип + данные.
    public Envelope(string type, object? payload)
        : this(type, NewId(), NowMs(), payload) { }

    // Два аргумента: тип + идентификатор.
    public Envelope(string type, string id)
        : this(type, id, NowMs(), null) { }
}
