using DeepBrain.Shared.BrainDtos.V3;

namespace DeepBrain.Host.BrainLife;

public sealed class PlanEngine
{
    public PlanDto? ActivePlan { get; private set; }
    private string _lastGoalId = "";

    public bool Created { get; private set; }
    public bool Interrupted { get; private set; }

    public PlanDto? Update(
        IReadOnlyList<GoalDto> goals,
        string dominantDrive,
        double loopPenalty,
        bool isSleeping,
        double selfPreservation)
    {
        Created = false;
        Interrupted = false;

        if (isSleeping)
        {
            ActivePlan = null;
            return ActivePlan;
        }

        var top = goals.OrderByDescending(g => g.Urgency).FirstOrDefault();
        var goalId = top?.Id ?? "focus";

        var goalChanged = goalId != _lastGoalId;
        if (selfPreservation > 0.85 || loopPenalty > 0.7)
        {
            Interrupted = ActivePlan != null;
            ActivePlan = null;
        }

        if (ActivePlan == null || ActivePlan.RemainingTicks <= 0 || goalChanged)
        {
            var strategy = MapGoalToStrategy(goalId, dominantDrive);
            ActivePlan = new PlanDto(strategy, TicksFor(strategy), goalId, $"goal={goalId} drive={dominantDrive}");
            _lastGoalId = goalId;
            Created = true;
        }
        else
        {
            ActivePlan = ActivePlan with { RemainingTicks = ActivePlan.RemainingTicks - 1 };
        }

        return ActivePlan;
    }

    private static string MapGoalToStrategy(string goalId, string drive)
    {
        return goalId switch
        {
            "regulate" => "regulate",
            "restore_energy" => "rest",
            "explore" => "explore",
            "connect" => "connect",
            "focus" => "focus",
            _ => drive == "exploration" ? "explore" : "focus"
        };
    }

    private static int TicksFor(string strategy)
    {
        return strategy switch
        {
            "rest" => 15,
            "regulate" => 8,
            "connect" => 8,
            "explore" => 10,
            "focus" => 12,
            _ => 8
        };
    }
}
