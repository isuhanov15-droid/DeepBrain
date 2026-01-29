using System.Text.Json;

namespace DeepBrain.Shared.Net;

public static class PayloadReader
{
    public static T Read<T>(object? payload)
    {
        if (payload is null)
            throw new InvalidOperationException("Envelope.Payload is null");

        if (payload is T already)
            return already;

        if (payload is JsonElement je)
            return je.Deserialize<T>(JsonWire.Options)
                   ?? throw new InvalidOperationException($"Cannot deserialize {typeof(T).Name} from JsonElement");

        if (payload is string s)
            return JsonSerializer.Deserialize<T>(s, JsonWire.Options)
                   ?? throw new InvalidOperationException($"Cannot deserialize {typeof(T).Name} from string");

        // Последний шанс: сериализуем обратно
        var json = JsonSerializer.Serialize(payload, JsonWire.Options);
        return JsonSerializer.Deserialize<T>(json, JsonWire.Options)
               ?? throw new InvalidOperationException($"Cannot deserialize {typeof(T).Name} from object");
    }
}
