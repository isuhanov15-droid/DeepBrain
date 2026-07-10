using System.Text.Json;

namespace DeepBrain.Shared.Net;

public static class PayloadReader
{
    public static T Read<T>(object? payload)
    {
        if (payload is null)
            throw new InvalidOperationException("Поле Envelope.Payload не содержит данных");

        if (payload is T already)
            return already;

        if (payload is JsonElement je)
            return je.Deserialize<T>(JsonWire.Options)
                   ?? throw new InvalidOperationException($"Не удалось десериализовать {typeof(T).Name} из JsonElement");

        if (payload is string s)
            return JsonSerializer.Deserialize<T>(s, JsonWire.Options)
                   ?? throw new InvalidOperationException($"Не удалось десериализовать {typeof(T).Name} из строки");

        // Последний шанс: сериализуем обратно
        var json = JsonSerializer.Serialize(payload, JsonWire.Options);
        return JsonSerializer.Deserialize<T>(json, JsonWire.Options)
               ?? throw new InvalidOperationException($"Не удалось десериализовать {typeof(T).Name} из объекта");
    }
}
