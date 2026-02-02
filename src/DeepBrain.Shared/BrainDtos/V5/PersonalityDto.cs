namespace DeepBrain.Shared.BrainDtos.V5;

public sealed record PersonalityDto(
    string PersonaId,
    double Warmth,
    double Fire,
    double Humor,
    double AnxietyBaseline,
    double CuriosityBaseline,
    double DisciplineBaseline,
    double AttachmentBaseline,
    double NoveltyPreference,
    double SafetyPreference,
    string[] ValuesTop,
    string[] StyleTags
);
