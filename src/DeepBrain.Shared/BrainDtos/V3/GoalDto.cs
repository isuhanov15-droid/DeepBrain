namespace DeepBrain.Shared.BrainDtos.V3;

public sealed record GoalDto(
    string Id,
    string Type,
    double Urgency,
    double Satisfaction,
    string Source
);
