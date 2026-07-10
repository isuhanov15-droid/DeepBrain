using System.Collections.Generic;

namespace DeepBrain.Shared.Localization;

/// <summary>
/// Преобразует стабильные машинные идентификаторы DeepBrain в русский текст
/// только для отображения. Значения протокола и DTO при этом не меняются.
/// </summary>
public static class RussianDisplay
{
    public const string NotAvailable = "нет данных";

    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["n/a"] = NotAvailable,
            ["none"] = "нет",
            ["unknown"] = "неизвестно",
            ["ok"] = "норма",
            ["fallback"] = "резервный выбор",
            ["mask_fallback"] = "резервный выбор по маске",
            ["no_allowed_actions"] = "нет разрешённых действий",
            ["cooldown_fallback"] = "резервный выбор из-за задержки",

            ["running"] = "работает",
            ["stopped"] = "остановлен",
            ["training"] = "обучение",
            ["evaluation"] = "оценка",
            ["dashboard"] = "панель",
            ["logs"] = "журнал",
            ["quiet"] = "без вывода",
            ["default"] = "по умолчанию",

            ["neutral"] = "нейтральное",
            ["anxious"] = "тревожное",
            ["calm"] = "спокойное",
            ["curious"] = "любопытное",
            ["tender"] = "мягкое",
            ["low"] = "сниженное",
            ["frustrated"] = "раздражённое",
            ["tired"] = "уставшее",
            ["focused"] = "сосредоточенное",

            ["wait"] = "ожидать",
            ["stop"] = "остановиться",
            ["step"] = "выполнить шаг",
            ["observe"] = "наблюдать",
            ["focus"] = "сосредоточиться",
            ["sleep"] = "сон",
            ["breathe_slow"] = "медленное дыхание",
            ["focus_narrow"] = "сузить фокус",
            ["rest_short"] = "короткий отдых",
            ["explore_signal"] = "исследовать сигнал",
            ["focus_widen"] = "расширить фокус",
            ["emit_message"] = "подать сигнал",
            ["reframe_negative"] = "переосмыслить негатив",
            ["recall_safe_memory"] = "вспомнить безопасный опыт",
            ["loop_break"] = "разорвать петлю",
            ["selftalk"] = "внутренняя речь",
            ["seeking contact"] = "ищу контакт",

            ["internal"] = "внутреннее",
            ["external"] = "внешнее",
            ["heuristic"] = "эвристика",
            ["net"] = "нейросеть",
            ["blend"] = "смешанная политика",
            ["stub"] = "заглушка",
            ["local"] = "локальный",
            ["remote"] = "удалённый",
            ["off"] = "выключен",

            ["regulate"] = "саморегуляция",
            ["rest"] = "восстановление",
            ["explore"] = "исследование",
            ["connect"] = "контакт",
            ["restore_energy"] = "восстановить энергию",
            ["self_preservation"] = "самосохранение",
            ["energy_conservation"] = "сбережение энергии",
            ["exploration"] = "исследование",
            ["attachment"] = "привязанность",
            ["agency"] = "самостоятельность",
            ["homeostatic"] = "гомеостатическая",
            ["exploratory"] = "исследовательская",
            ["social"] = "социальная",
            ["energy"] = "энергия",

            ["threat"] = "угроза",
            ["novelty"] = "новизна",
            ["body"] = "тело",
            ["baseline"] = "базовое состояние",
            ["self_preservation/threat_event"] = "самосохранение / событие угрозы",
            ["exploration/calm_window"] = "исследование / окно спокойствия",
            ["attachment/social_ping"] = "привязанность / сигнал контакта",
            ["fatigue/sleep_pressure"] = "усталость / потребность во сне",
            ["agency/negative_valence"] = "самостоятельность / отрицательная окраска",
            ["goal=explore"] = "цель — исследование",
            ["goal=work"] = "цель — работа",

            ["morning"] = "утро",
            ["active"] = "активный период",
            ["evening"] = "вечер",
            ["night"] = "ночь",
            ["fiery"] = "огненный",
            ["witty"] = "ироничный",

            ["repeat"] = "повторение",
            ["abab"] = "чередование ABAB",
            ["stuck_state"] = "застрявшее состояние",
            ["manual"] = "ручной сброс",
            ["scenario_complete"] = "сценарий завершён",
            ["timeout"] = "лимит времени",
            ["panic"] = "паническое состояние",
            ["loop"] = "петля",

            ["calm_baseline"] = "спокойная база",
            ["novelty_walk"] = "исследование новизны",
            ["threat_pulses"] = "импульсы угрозы",
            ["social_pull"] = "социальное притяжение",
            ["fatigue_day"] = "день усталости",
            ["mixed_adaptive"] = "смешанный адаптивный",
            ["fixed"] = "фиксированный",
            ["round_robin"] = "по очереди",
            ["reward_gated"] = "по порогу награды",
            ["random_seeded"] = "псевдослучайный",

            ["threat_spike"] = "всплеск угрозы",
            ["micro_threat"] = "малая угроза",
            ["novelty_opportunity"] = "возможность исследования",
            ["social_ping"] = "сигнал контакта",
            ["fatigue_wave"] = "волна усталости",
            ["calm_window"] = "окно спокойствия",
            ["sudden danger"] = "внезапная опасность",
            ["minor risk"] = "небольшой риск",
            ["new pattern"] = "новый образец",
            ["call from distance"] = "далёкий зов",
            ["energy dip"] = "спад энергии",
            ["safe window"] = "безопасное окно",

            ["regulate_breathe"] = "успокоить дыхание",
            ["rest_recover"] = "отдых и восстановление",
            ["check_social"] = "проверить контакт",
            ["explore_window"] = "исследовать окно возможностей",
            ["break_loop"] = "разорвать петлю",
            ["threat_high"] = "высокая угроза",
            ["fatigue_high"] = "сильная усталость",
            ["evening_social_ping"] = "вечерний сигнал контакта",
            ["calm_novelty_window"] = "спокойное окно новизны",
            ["loop_warning"] = "предупреждение о петле",

            ["care"] = "забота",
            ["truth"] = "правда",
            ["growth"] = "развитие",
            ["loyalty"] = "верность",
            ["warm"] = "тёплый",
            ["direct"] = "прямой",
            ["metaphor-lite"] = "лёгкие метафоры",
            ["ml.enable=false (brainconfig)"] = "ml.enable=false в brainconfig",
            ["backend=off"] = "backend отключён",
            ["ML.Core not linked: set ML_CORE_PATH to ML.Core.csproj"] =
                "ML.Core не подключён: укажите ML_CORE_PATH до ML.Core.csproj",
            ["remote not connected: run mlconnect or check host/port"] =
                "удалённый ML не подключён: выполните mlconnect или проверьте адрес и порт"
        };

    public static string Token(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return NotAvailable;

        var text = value.Trim();
        if (Names.TryGetValue(text, out var translated))
            return translated;

        if (text.StartsWith("inertia:", StringComparison.OrdinalIgnoreCase))
            return $"инерция: {Token(text["inertia:".Length..])}";

        if (text.Contains('|'))
            return string.Join(" / ", text.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Token));

        return text;
    }

    public static string YesNo(bool value) => value ? "да" : "нет";
}
