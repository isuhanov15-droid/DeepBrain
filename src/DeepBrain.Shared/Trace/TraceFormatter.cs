using System.Text.Json;
using DeepBrain.Shared.Net;

namespace DeepBrain.Shared.Trace;

public static class TraceFormatter
{
    public static string FormatCompact(TraceDto trace)
    {
        if (trace.Data is string line)
            return line;

        if (!TryGetElement(trace.Data, out var data))
            return $"trace[{trace.Stage}]";

        return trace.Stage switch
        {
            "decision" => FormatDecision(data),
            "reward" => FormatReward(data),
            "tick" => GetString(data, "line") ?? CompactJson(trace.Stage, data),
            "episode.reset" => GetString(data, "line") ?? CompactJson(trace.Stage, data),
            "scenario" => GetString(data, "line") ?? CompactJson(trace.Stage, data),
            "selftalk" => $"selftalk: {GetString(data, "text") ?? "n/a"}",
            "output" => $"output action={GetString(data, "actionName") ?? "n/a"} msg={GetString(data, "message") ?? ""}".Trim(),
            "loop.detected" => $"loop type={GetString(data, "type") ?? "none"} streak={GetInt64(data, "streak")} strength={GetDouble(data, "strength"):0.00}",
            _ => CompactJson(trace.Stage, data)
        };
    }

    private static string FormatDecision(JsonElement data)
    {
        var action = GetString(data, "actionName") ?? "n/a";
        var kind = GetString(data, "kind") ?? "n/a";
        var reason = GetString(data, "reason") ?? "n/a";
        var mask = GetString(data, "mask") ?? "ok";
        return $"decision act={action} kind={kind} reason={reason} mask={mask}";
    }

    private static string FormatReward(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.String)
            return data.GetString() ?? "reward";

        var line = GetString(data, "line");
        if (!string.IsNullOrWhiteSpace(line))
            return line!;

        return CompactJson("reward", data);
    }

    private static string CompactJson(string stage, JsonElement data)
    {
        var json = data.GetRawText();
        return $"trace[{stage}]: {json}";
    }

    private static bool TryGetElement(object? value, out JsonElement element)
    {
        switch (value)
        {
            case JsonElement json:
                element = json;
                return true;
            case null:
                element = default;
                return false;
            default:
                element = JsonSerializer.SerializeToElement(value, JsonWire.Options);
                return true;
        }
    }

    private static string? GetString(JsonElement data, string name)
    {
        return data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;
    }

    private static long GetInt64(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        return long.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private static double GetDouble(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var value))
            return 0.0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        return double.TryParse(value.ToString(), out var parsed) ? parsed : 0.0;
    }
}
