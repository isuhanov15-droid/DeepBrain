# DeepBrain v1.0 Regression Checklist

## Build and Test
- Run `dotnet build DeepBrain.slnx`.
- Run `dotnet test tests/DeepBrain.Tests/DeepBrain.Tests.csproj`.

## Host and Studio
- Start `DeepBrain.Host`.
- Start `DeepBrain.Studio`.
- Connect Studio to `127.0.0.1:5555`.
- Confirm `Life`, `Output`, `Trace`, and `Logs` tabs update without overlap.

## Scenario and Curriculum
- Run `scenario.list` in Host and verify scenarios are printed.
- Run `scenario.set calm_baseline` and confirm `scenario=calm_baseline` in Life telemetry.
- Run `curriculum.mode round_robin` and `curriculum.next`; confirm scenario index changes.

## ML and Evaluation
- Run `ml.mode training`, then `mlstatus`; verify mode is `training`.
- Run `ml.mode evaluation`, then `mlstatus`; verify epsilon is forced to zero and training is disabled.
- If ML is unavailable, verify `mlstatus` explains why backend is `stub` or disabled.

## Episode Flow
- Let at least one episode finish naturally or run `episode.reset`.
- Confirm `reports/episodes/YYYY-MM-DD/episodes.jsonl` receives a new JSON line.
- Confirm Host logs `EPISODE_RESET`.
- Confirm the next episode starts automatically.

## Trace, Logs, Output
- Confirm compact tick traces continue to arrive: `tick=... ep=... act=... reward=...`.
- Confirm reward trace always includes `tot/h/x/s/lp/ia`.
- Confirm `Output` receives self-talk and external output messages.
- Disconnect and reconnect Studio; confirm state, trace, logs, and output recover cleanly.
