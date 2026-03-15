# Telemetry Reference

## Life UI
The Life panel shows the stable operator-facing telemetry.

### Evaluation ratios
- `calmRatio`
- `anxiousRatio`
- `curiousRatio`

These are always clamped to `0..1` and are the only calm/anxious/curious values shown in the main `eval:` line:

```text
eval: reward=0.013 loops=1 diversity=0.42 calm=0.81 anxious=0.04 curious=0.15
```

### Evaluation counts
- `calmCount`
- `anxiousCount`
- `curiousCount`
- `loopCount`

These stay in DTO/report payloads and are not shown in the main eval line.

### Rolling stats scores
- `calmScore`
- `anxiousScore`
- `curiousScore`

These belong to the short rolling `stats` snapshot and are separate from evaluation ratios. They are displayed as `anxScore/calmScore/curiousScore` to avoid ambiguity.

## Reward
Stable reward breakdown fields:
- `homeostasis`
- `explore`
- `social`
- `loopPenalty`
- `invalidActionPenalty`
- `total`

## Loop
Stable loop telemetry:
- `isInLoop`
- `type`
- `strength`
- `observedCount`
- `currentPenalty`

## Console / Trace
Human-readable console trace lines use compact formatting:
- `tick=...`
- `decision act=...`
- `reward tot=...`
- `episode=...`
- `scenario=...`

Machine-readable JSON trace remains available in persisted trace files.

## Internal / Report Fields
The following fields remain available for reports and diagnostics:
- action histograms
- mood distributions
- evaluation counts
- reward breakdown averages
- scenario score
- ML telemetry
