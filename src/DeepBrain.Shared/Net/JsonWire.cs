using System.Text.Json;

namespace DeepBrain.Shared.Net;

public static class JsonWire
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static byte[] Serialize(Envelope env) =>
        JsonSerializer.SerializeToUtf8Bytes(env, Options);

    public static Envelope Deserialize(byte[] bytes) =>
        JsonSerializer.Deserialize<Envelope>(bytes, Options)
        ?? throw new InvalidDataException("Не удалось десериализовать сетевой конверт Envelope.");
}
