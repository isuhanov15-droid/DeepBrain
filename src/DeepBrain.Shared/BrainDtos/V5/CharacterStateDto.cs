namespace DeepBrain.Shared.BrainDtos.V5;

public sealed record CharacterStateDto(
    PersonalityDto Personality,
    IReadOnlyList<HabitDto> HabitsTop,
    string? ActiveHabitId,
    double HabitInfluence,
    string VoiceMode,
    int EmitCooldownRemaining,
    long LastEmitTick,
    int ConsumedEventsCount
);
