using System.Text.Json;
using DeepBrain.Shared.Localization;
using DeepBrain.Shared.Net;

namespace DeepBrain.Shared.Trace;

public static class TraceFormatter
{
    public static string FormatCompact(TraceDto trace)
    {
        if (trace.Data is string line)
            return line;

        if (!TryGetElement(trace.Data, out var data))
            return $"трассировка[{trace.Stage}]";

        return trace.Stage switch
        {
            "decision" => FormatDecision(data),
            "reward" => FormatReward(data),
            "tick" => GetString(data, "line") ?? CompactJson(trace.Stage, data),
            "episode.reset" => GetString(data, "line") ?? CompactJson(trace.Stage, data),
            "scenario" => GetString(data, "line") ?? CompactJson(trace.Stage, data),
            "world.climate" => FormatClimate(data),
            "homeostasis" => FormatHomeostasis(data),
            "instincts" => FormatInstincts(data),
            "affect" => FormatAffect(data),
            "action" => FormatAction(data),
            "ml.train" => $"обучение ML шаг={GetInt64(data, "step")} ошибка={GetDouble(data, "loss"):0.000}",
            "selftalk" => $"внутренняя речь: {GetString(data, "text") ?? RussianDisplay.NotAvailable}",
            "output" => $"вывод действие={RussianDisplay.Token(GetString(data, "actionName"))} " +
                        $"сообщение={GetString(data, "message") ?? string.Empty}".Trim(),
            "loop.detected" => $"петля тип={RussianDisplay.Token(GetString(data, "type"))} " +
                               $"серия={GetInt64(data, "streak")} сила={GetDouble(data, "strength"):0.00}",
            _ => CompactJson(trace.Stage, data)
        };
    }

    private static string FormatDecision(JsonElement data)
    {
        var action = RussianDisplay.Token(GetString(data, "actionName"));
        var kind = RussianDisplay.Token(GetString(data, "kind"));
        var reason = RussianDisplay.Token(GetString(data, "reason"));
        var mask = RussianDisplay.Token(GetString(data, "mask") ?? "ok");
        return $"решение действие={action} тип={kind} причина={reason} маска={mask}";
    }

    private static string FormatReward(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.String)
            return data.GetString() ?? "награда";

        var line = GetString(data, "line");
        if (!string.IsNullOrWhiteSpace(line))
            return line!;

        return CompactJson("награда", data);
    }

    private static string FormatClimate(JsonElement data)
    {
        return $"климат спокойствие={GetDouble(data, "calm"):0.00} " +
               $"стресс={GetDouble(data, "stress"):0.00} " +
               $"напряжение={GetDouble(data, "tension"):0.00} " +
               $"база={GetDouble(data, "baseline"):0.00} " +
               $"последнее событие={RussianDisplay.Token(GetString(data, "lastMajor"))}";
    }

    private static string FormatHomeostasis(JsonElement data)
    {
        return $"гомеостаз энергия={GetDouble(data, "energy"):0.00} " +
               $"усталость={GetDouble(data, "fatigue"):0.00} " +
               $"возбуждение={GetDouble(data, "arousal"):0.00} " +
               $"боль={GetDouble(data, "pain"):0.00} " +
               $"безопасность={GetDouble(data, "safety"):0.00}";
    }

    private static string FormatInstincts(JsonElement data)
    {
        return $"инстинкты самосохранение={GetDouble(data, "selfPreservation"):0.00} " +
               $"энергосбережение={GetDouble(data, "energyConservation"):0.00} " +
               $"исследование={GetDouble(data, "exploration"):0.00} " +
               $"привязанность={GetDouble(data, "attachment"):0.00} " +
               $"самостоятельность={GetDouble(data, "agency"):0.00}";
    }

    private static string FormatAffect(JsonElement data)
    {
        return $"аффект настроение={RussianDisplay.Token(GetString(data, "mood"))} " +
               $"валентность={GetDouble(data, "valence"):0.00} " +
               $"возбуждение={GetDouble(data, "arousal"):0.00}";
    }

    private static string FormatAction(JsonElement data)
    {
        return $"действие={RussianDisplay.Token(GetString(data, "actionName"))} " +
               $"тип={RussianDisplay.Token(GetString(data, "kind"))}";
    }

    private static string CompactJson(string stage, JsonElement data)
    {
        var json = data.GetRawText();
        return $"трассировка[{stage}]: {json}";
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
