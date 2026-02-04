using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Shared.MlBridge;

public sealed class FlexibleBoolArrayConverter : JsonConverter<bool[]>
{
    public override bool[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Array.Empty<bool>();
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected JSON array for bool[]");

        var list = new List<bool>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return list.ToArray();
            list.Add(ReadBool(ref reader));
        }

        throw new JsonException("Unexpected end of JSON while reading bool[]");
    }

    public override void Write(Utf8JsonWriter writer, bool[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        for (var i = 0; i < value.Length; i++)
            writer.WriteBooleanValue(value[i]);
        writer.WriteEndArray();
    }

    private static bool ReadBool(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var n))
                    return n != 0;
                if (reader.TryGetDouble(out var d))
                    return Math.Abs(d) > double.Epsilon;
                break;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return false;
                if (bool.TryParse(s, out var b))
                    return b;
                if (long.TryParse(s, out var l))
                    return l != 0;
                if (double.TryParse(s, out var f))
                    return Math.Abs(f) > double.Epsilon;
                break;
        }

        throw new JsonException($"Invalid bool value token: {reader.TokenType}");
    }
}
