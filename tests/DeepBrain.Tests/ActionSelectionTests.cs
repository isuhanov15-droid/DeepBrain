using DeepBrain.Host.BrainLife;
using DeepBrain.Host.BrainLife.Ml;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.BrainDtos.V6;
using Xunit;

namespace DeepBrain.Tests;

public sealed class ActionSelectionTests
{
    [Fact]
    public void FocusStrategyProducesExecutableCandidates()
    {
        var selector = new ActionSelector();
        var (candidates, strategy) = selector.BuildCandidates(
            new HomeostasisDto(0.8, 0.2, 0.2, 0.1, 0.8),
            new InstinctsDto(0.2, 0.2, 0.2, 0.2, 0.2),
            new AffectDto("calm", 0.1, 0.2),
            new LearningEngine(),
            new LoopDetector(),
            new EpisodeMemory(),
            new ActionCooldowns(),
            tick: 10,
            habitAction: null,
            habitStrength: 0,
            habitInfluence: 0,
            emitCooldownTicks: 60,
            allowVariety: false,
            calmExploreBoost: false,
            dominantDrive: "agency",
            planStrategy: null,
            attentionFocus: "baseline",
            semantic: new SemanticMemory(),
            semanticKey: "test",
            appraisal: new AppraisalDto(0.1, 0.1, 0.1, 0.1),
            actionsConfig: BrainConfig.Default.Actions);

        Assert.Equal("focus", strategy);
        Assert.Contains(candidates, c => c.Action.Name == "focus_narrow");
        Assert.Contains(candidates, c => c.Action.Name == "focus_widen");
    }

    [Fact]
    public void RecoveryCandidatesContainEveryAllowedAction()
    {
        var selector = new ActionSelector();
        var candidates = selector.BuildRecoveryCandidates(
            new[] { "focus_widen", "rest_short" },
            new LearningEngine(),
            new ActionCooldowns(),
            tick: 20);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.Action.Name == "focus_widen");
        Assert.Contains(candidates, c => c.Action.Name == "rest_short");
        Assert.All(candidates, c => Assert.Equal("mask_fallback", c.Reason));
    }

    [Fact]
    public void RestCooldownKeepsNeutralFocusCandidates()
    {
        var cooldowns = new ActionCooldowns();
        cooldowns.Mark("rest_short", 10);
        var selector = new ActionSelector();

        var (candidates, strategy) = selector.BuildCandidates(
            new HomeostasisDto(0.4, 0.8, 0.2, 0.1, 0.8),
            new InstinctsDto(0.2, 0.9, 0.2, 0.2, 0.2),
            new AffectDto("tired", -0.1, 0.2),
            new LearningEngine(),
            new LoopDetector(),
            new EpisodeMemory(),
            cooldowns,
            tick: 11,
            habitAction: null,
            habitStrength: 0,
            habitInfluence: 0,
            emitCooldownTicks: 60,
            allowVariety: false,
            calmExploreBoost: false,
            dominantDrive: "energy_conservation",
            planStrategy: null,
            attentionFocus: "body",
            semantic: new SemanticMemory(),
            semanticKey: "test",
            appraisal: new AppraisalDto(0.1, 0.1, 0.1, 0.9),
            actionsConfig: BrainConfig.Default.Actions);

        Assert.Equal("rest", strategy);
        Assert.DoesNotContain(candidates, c => c.Action.Name == "rest_short");
        Assert.Contains(candidates, c => c.Action.Name == "focus_narrow");
        Assert.Contains(candidates, c => c.Action.Name == "focus_widen");
    }

    [Fact]
    public void ActionMaskAlwaysLeavesNeutralRecoveryAction()
    {
        var cooldowns = new ActionCooldowns();
        foreach (var action in ActionCatalog.Actions)
            cooldowns.Mark(action, 10);

        var cooldownConfig = new ActionsConfig(
            BreatheSlow: 100,
            RestShort: 100,
            ReframeNegative: 100,
            FocusNarrow: 100,
            FocusWiden: 100,
            ExploreSignal: 100,
            EmitMessage: 100,
            EmitCooldownCalm: 100,
            EmitCooldownTender: 100,
            EmitCooldownWitty: 100,
            EmitCooldownFiery: 100);

        var mask = new ActionMasker().BuildMask(
            new HomeostasisDto(1.0, 0.0, 1.0, 0.0, 0.8),
            new AffectDto("calm", 1.0, 1.0),
            cooldowns,
            tick: 11,
            emitCooldownTicks: 100,
            actions: cooldownConfig,
            allowLoopBreak: false);

        Assert.Equal(1f, mask[ActionCatalog.IndexOf("focus_widen")]);
        Assert.Single(mask.Where(v => v > 0f));
    }

    [Fact]
    public void CriticalSafetyForcesBreathingEvenWhenCalmAndOnCooldown()
    {
        var cooldowns = new ActionCooldowns();
        cooldowns.Mark("breathe_slow", 10);
        var homeostasis = new HomeostasisDto(0.8, 0.2, 0.0, 0.0, 0.02);
        var affect = new AffectDto("calm", 0.2, 0.0);
        var selector = new ActionSelector();

        var (candidates, _) = selector.BuildCandidates(
            homeostasis,
            new InstinctsDto(0.2, 0.2, 0.2, 0.2, 0.2),
            affect,
            new LearningEngine(),
            new LoopDetector(),
            new EpisodeMemory(),
            cooldowns,
            tick: 11,
            habitAction: null,
            habitStrength: 0,
            habitInfluence: 0,
            emitCooldownTicks: 60,
            allowVariety: false,
            calmExploreBoost: false,
            dominantDrive: "agency",
            planStrategy: null,
            attentionFocus: "baseline",
            semantic: new SemanticMemory(),
            semanticKey: "test",
            appraisal: new AppraisalDto(0.1, 0.1, 0.1, 0.1),
            actionsConfig: BrainConfig.Default.Actions);
        var mask = new ActionMasker().BuildMask(
            homeostasis,
            affect,
            cooldowns,
            tick: 11,
            emitCooldownTicks: 60,
            actions: BrainConfig.Default.Actions,
            allowLoopBreak: false);

        Assert.Contains(candidates, c => c.Action.Name == "breathe_slow");
        Assert.Equal(1f, mask[ActionCatalog.IndexOf("breathe_slow")]);
        Assert.Single(mask.Where(value => value > 0));
    }

    [Fact]
    public void PendingSocialSignalCreatesResponseCandidateDespiteLowAttachment()
    {
        var selector = new ActionSelector();

        var (candidates, _) = selector.BuildCandidates(
            new HomeostasisDto(0.8, 0.2, 0.2, 0.1, 0.8),
            new InstinctsDto(0.2, 0.2, 0.2, 0.05, 0.2),
            new AffectDto("calm", 0.1, 0.2),
            new LearningEngine(),
            new LoopDetector(),
            new EpisodeMemory(),
            new ActionCooldowns(),
            tick: 10,
            habitAction: null,
            habitStrength: 0,
            habitInfluence: 0,
            emitCooldownTicks: 60,
            allowVariety: false,
            calmExploreBoost: false,
            dominantDrive: "agency",
            planStrategy: "focus",
            attentionFocus: "social",
            semantic: new SemanticMemory(),
            semanticKey: "test",
            appraisal: new AppraisalDto(0.1, 0.1, 0.9, 0.1),
            actionsConfig: BrainConfig.Default.Actions,
            hasPendingSocialSignal: true);

        var response = Assert.Single(candidates.Where(c => c.Action.Name == "emit_message"));
        Assert.Equal("social_signal", response.Reason);
        Assert.True(response.Action.Strength >= 0.75);
    }
}
