using System.Linq;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V3;
using DeepBrain.Shared.BrainDtos.V5;
using DeepBrain.Shared.BrainDtos.V4;

namespace DeepBrain.Host.BrainLife;

public sealed class PersonalityProfile
{
    public PersonalityDto Persona { get; }

    private PersonalityProfile(PersonalityDto persona)
    {
        Persona = persona;
    }

    public static PersonalityProfile LoadLada()
    {
        var persona = new PersonalityDto(
            "lada",
            Warmth: 0.75,
            Fire: 0.55,
            Humor: 0.35,
            AnxietyBaseline: 0.35,
            CuriosityBaseline: 0.55,
            DisciplineBaseline: 0.6,
            AttachmentBaseline: 0.5,
            NoveltyPreference: 0.55,
            SafetyPreference: 0.65,
            ValuesTop: new[] { "care", "truth", "growth", "loyalty" },
            StyleTags: new[] { "warm", "direct", "metaphor-lite" }
        );

        return new PersonalityProfile(persona);
    }

    public void ApplyBaselines(ref AffectDto affect, ref InstinctsDto instincts)
    {
        instincts = instincts with
        {
            SelfPreservation = LifeMath.Clamp01(instincts.SelfPreservation + 0.15 * Persona.AnxietyBaseline),
            Exploration = LifeMath.Clamp01(instincts.Exploration + 0.15 * Persona.CuriosityBaseline),
            Attachment = LifeMath.Clamp01(instincts.Attachment + 0.15 * Persona.AttachmentBaseline),
            EnergyConservation = LifeMath.Clamp01(instincts.EnergyConservation + 0.1 * Persona.SafetyPreference)
        };

        affect = affect with
        {
            Arousal = LifeMath.Clamp01(affect.Arousal + 0.08 * Persona.AnxietyBaseline),
            Valence = Math.Clamp(affect.Valence + (Persona.Warmth - 0.5) * 0.05, -1, 1)
        };
    }

    public IReadOnlyList<GoalDto> BiasGoals(IReadOnlyList<GoalDto> goals, string phase, IReadOnlyList<WorldEventDto> eventsList)
    {
        if (goals.Count == 0) return goals;
        var list = goals.ToList();
        var hasSocial = eventsList.Any(e => e.Type == "social_ping" && e.Salience > 0.3);

        for (var i = 0; i < list.Count; i++)
        {
            var g = list[i];
            var urgency = g.Urgency;
            if (g.Id == "regulate")
                urgency = LifeMath.Clamp01(urgency + 0.1 * Persona.SafetyPreference);
            if (g.Id == "restore_energy")
                urgency = LifeMath.Clamp01(urgency + 0.05 * Persona.DisciplineBaseline);
            if (g.Id == "explore")
                urgency = LifeMath.Clamp01(urgency + 0.1 * Persona.NoveltyPreference);
            if (g.Id == "connect" && (phase == "evening" || hasSocial))
                urgency = LifeMath.Clamp01(urgency + 0.12 * Persona.AttachmentBaseline);

            list[i] = g with { Urgency = urgency };
        }

        return list;
    }
}
