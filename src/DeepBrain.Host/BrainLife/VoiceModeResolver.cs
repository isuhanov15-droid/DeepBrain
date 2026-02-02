using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V5;
using DeepBrain.Shared.BrainDtos.V3;

namespace DeepBrain.Host.BrainLife;

public sealed class VoiceModeResolver
{
    public string Resolve(PersonalityDto persona, AffectDto affect, InstinctsDto instincts, CircadianDto circadian)
    {
        if (circadian.Phase is "evening" or "night" && persona.AttachmentBaseline > 0.45)
            return "tender";

        if (persona.Fire > 0.6 && instincts.Agency > 0.6)
            return "fiery";

        if (affect.Arousal < 0.35 && persona.Humor > 0.4)
            return "witty";

        return "calm";
    }
}
