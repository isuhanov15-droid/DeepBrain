# Cortex action advice (1.4.1)

The inner voice can now recommend one catalog action using the optional
`suggested_action` insight field. Old five-field responses and journals remain
readable and default to `none`. Free-form speech is never parsed as a command.
The model receives the source frame's actual candidates; its response schema is
restricted to those candidates plus `none`. Availability is checked again when
the delayed answer reaches the current decision.

Enable explicitly in `llm`:

```json
{
  "actionAdviceEnabled": true,
  "actionAdviceMaxAgeTicks": 600,
  "actionAdviceMinConfidence": 0.6,
  "actionAdviceMaxBias": 0.15
}
```

On each awake decision the advisor adds `maxBias * confidence` to the suggested
action's existing heuristic score, before the unchanged ML blend and exploration.
The maximum configurable bias is 0.25. Candidates, cooldowns and masks still apply.
Advice is withheld during sleep, safety below 0.35 or pain above 0.65. Age is
measured from the source frame, including inference latency. Run, episode,
scenario and sleep transitions invalidate the source context, including replies
that arrive after a transition. Only fresh callbacks populate the advisor; journal
replay never restores executable advice.

`state.decision.cortexAdvice` reports status, suggestion, actual selection,
confidence, applied score bias and source frame age. `applied` means scores were
biased; `matchesSelection` means the selected action matched. Neither is a claim
that the recommendation caused that choice. Web Studio 0.2 displays this distinction.

The trained model remains 38 inputs / 8 actions. Checkpoint, episodic memory,
scenario experience and habit formats and paths are unchanged. Existing weight-only
checkpoint limitations remain: optimizer and replay buffer are not restored.

Validation: contract compatibility and allowlist, expiry boundaries, stale/future
frames, out-of-order callbacks, context changes, sleep and recovery guards,
confidence, unavailable actions, bounded bias, default-off behavior, an actual
ML-blended choice changed by advice, and Ollama schema-to-callback integration.
Run `dotnet test tests/DeepBrain.Tests/DeepBrain.Tests.csproj -c Release`.

Deployment: publish into a new runtime directory; gracefully stop the host before
ML so the existing checkpoint can flush. Verify and archive the production memory,
checkpoint chain, configuration and launcher before switching. Preserve absolute
data paths. Rollback selects the old binary and configuration against the current
compatible data; do not restore an older data backup over newer experience.
