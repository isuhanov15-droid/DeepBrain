using DeepBrain.Host.Brain.Perception;
using DeepBrain.Host.Brain.State;

namespace DeepBrain.Host.Brain.Policy;

public sealed class PolicyEngine
{
    public Decision Decide(Percept percept, BrainStateInternal state)
    {
        var input = percept.Input;

        // 1) Команда имеет приоритет
        var cmd = (input.Command ?? "").Trim().ToLowerInvariant();
        if (cmd == "stop")
            return new Decision("stop", 1.0f, "внешняя команда: stop");

        if (cmd == "step")
            return new Decision("step", 1.0f, "внешняя команда: step");

        // 2) Реакция на стресс/энергию
        if (input.Stress > 0.7f)
            return new Decision("calm", 0.9f, $"высокий стресс: {input.Stress:0.00}");

        if (input.Energy < 0.3f)
            return new Decision("rest", 0.9f, $"низкая энергия: {input.Energy:0.00}");

        // 3) Цель
        var goal = (input.Goal ?? "").Trim().ToLowerInvariant();
        if (goal == "explore")
            return new Decision("observe", 0.7f, "goal=explore");

        if (goal == "work")
            return new Decision("focus", 0.7f, "goal=work");

        // 4) Фоллбек
        var name = (percept.Tick % 2 == 0) ? "observe" : "wait";
        return new Decision(name, 0.4f, "резервный выбор: чётность тика");
    }
}
