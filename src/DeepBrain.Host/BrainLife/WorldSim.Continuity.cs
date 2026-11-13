// Explicit runtime checkpoint schema v1. Keep new behavior state in this contract.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepBrain.Host.BrainLife;

public sealed partial class WorldSim
{
    private sealed class ContinuityState
    {
        [JsonRequired] public long LastThreatTick { get; set; } = default!;
        [JsonRequired] public string LastMajorEvent { get; set; } = default!;
        [JsonRequired] public double Noise { get; set; } = default!;
        [JsonRequired] public double Novelty { get; set; } = default!;
        [JsonRequired] public double SocialPresence { get; set; } = default!;
        [JsonRequired] public double Threat { get; set; } = default!;
        [JsonRequired] public double Tension { get; set; } = default!;
        [JsonRequired] public double BaselineTension { get; set; } = default!;
        [JsonRequired] public double DriftRatePerSec { get; set; } = default!;
        [JsonRequired] public double ShockChanceBase { get; set; } = default!;
        [JsonRequired] public double MicroThreatChanceBase { get; set; } = default!;
        [JsonRequired] public double NoveltyChanceBase { get; set; } = default!;
        [JsonRequired] public double SocialPingChanceBase { get; set; } = default!;
        [JsonRequired] public double FatigueWaveChanceBase { get; set; } = default!;
        [JsonRequired] public double CalmWindowChanceBase { get; set; } = default!;
        [JsonRequired] public double BaselineThreat { get; set; } = default!;
        [JsonRequired] public double ThreatReturnRatePerSec { get; set; } = default!;
        [JsonRequired] public ulong RandomState { get; set; }
    }

    internal JsonElement CaptureRuntimeState()
    {
        return JsonSerializer.SerializeToElement(new ContinuityState
        {
            LastThreatTick = _lastThreatTick,
            LastMajorEvent = _lastMajorEvent,
            Noise = Noise,
            Novelty = Novelty,
            SocialPresence = SocialPresence,
            Threat = Threat,
            Tension = Tension,
            BaselineTension = BaselineTension,
            DriftRatePerSec = DriftRatePerSec,
            ShockChanceBase = ShockChanceBase,
            MicroThreatChanceBase = MicroThreatChanceBase,
            NoveltyChanceBase = NoveltyChanceBase,
            SocialPingChanceBase = SocialPingChanceBase,
            FatigueWaveChanceBase = FatigueWaveChanceBase,
            CalmWindowChanceBase = CalmWindowChanceBase,
            BaselineThreat = BaselineThreat,
            ThreatReturnRatePerSec = ThreatReturnRatePerSec,
            RandomState = ((ContinuityRandom)_rng).State
        }, RuntimeCheckpoint.Json);
    }

    internal void RestoreRuntimeState(JsonElement json)
    {
        var v = RuntimeCheckpoint.Deserialize<ContinuityState>(json);

        _rng = new ContinuityRandom(v.RandomState);
        _lastThreatTick = v.LastThreatTick;
        _lastMajorEvent = v.LastMajorEvent;
        Noise = v.Noise;
        Novelty = v.Novelty;
        SocialPresence = v.SocialPresence;
        Threat = v.Threat;
        Tension = v.Tension;
        BaselineTension = v.BaselineTension;
        DriftRatePerSec = v.DriftRatePerSec;
        ShockChanceBase = v.ShockChanceBase;
        MicroThreatChanceBase = v.MicroThreatChanceBase;
        NoveltyChanceBase = v.NoveltyChanceBase;
        SocialPingChanceBase = v.SocialPingChanceBase;
        FatigueWaveChanceBase = v.FatigueWaveChanceBase;
        CalmWindowChanceBase = v.CalmWindowChanceBase;
        BaselineThreat = v.BaselineThreat;
        ThreatReturnRatePerSec = v.ThreatReturnRatePerSec;
    }

    internal void EnableContinuityRandom() => _rng = new ContinuityRandom(_continuitySeed);
}
