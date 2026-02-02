namespace DeepBrain.Host.BrainLife;

public sealed class LadaStyleBank
{
    public string Pick(string category, string voiceMode, long tick)
    {
        var list = GetList(category, voiceMode);
        if (list.Length == 0) return string.Empty;
        var index = (int)((1103515245L * (tick + StableHash(category)) + 12345) & int.MaxValue) % list.Length;
        return list[index];
    }

    private static long StableHash(string text)
    {
        unchecked
        {
            long hash = 5381;
            foreach (var ch in text)
                hash = ((hash << 5) + hash) + ch;
            return hash;
        }
    }

    private static string[] GetList(string category, string voiceMode)
    {
        return category switch
        {
            "anxious" => voiceMode switch
            {
                "tender" => new[]
                {
                    "Я в тревоге. Сужаю фокус и дышу.",
                    "Сейчас главное — безопасность. Дышу ровно."
                },
                "witty" => new[]
                {
                    "Тревожно. Беру паузу и дышу.",
                    "Панику выключаю. Дышу."
                },
                "fiery" => new[]
                {
                    "Тревога. Сжимаю фокус. Дышу.",
                    "Сначала безопасность. Дышу."
                },
                _ => new[]
                {
                    "Я в тревоге. Сужаю фокус и дышу.",
                    "Сейчас главное — безопасность. Дышу ровно."
                }
            },
            "loop" => voiceMode switch
            {
                "fiery" => new[] { "Стоп. Это петля. Переключаюсь." },
                "witty" => new[] { "Зациклилась. Меняю рисунок." },
                _ => new[] { "Я зациклилась. Меняю подход." }
            },
            "calm_window" => new[]
            {
                "Окно спокойствия. Можно исследовать.",
                "Тихо. Можно шагнуть дальше."
            },
            "connect" => voiceMode switch
            {
                "tender" => new[] { "Тянет к связи. Подаю сигнал." },
                _ => new[] { "Хочу контакта. Мягко стучу." }
            },
            "rest" => new[]
            {
                "Восстановление важнее гонки. Небольшой отдых.",
                "Сейчас отдых — правильно."
            },
            _ => Array.Empty<string>()
        };
    }
}
