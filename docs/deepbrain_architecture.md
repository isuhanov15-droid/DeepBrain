# DeepBrain v1.0 Architecture

## Overview
DeepBrain v1.0 is a deterministic autonomous core. It owns world simulation, internal regulation, action selection, episode accounting, and telemetry. Studio is read-only and the ML layer is optional through interfaces.

## LifeLoop
- `LifeLoop` is the only runtime orchestrator for the modern brain path.
- Each tick reads the current config snapshot, advances circadian time, updates world state, homeostasis, appraisal, attention, affect, goals, plans, masking, action selection, reward, learning, memory, and telemetry.
- The loop does not depend on Studio and talks to transport through delegates.
- ML is consumed only through `IMlPolicyAdvisor`.

## EpisodeManager
- Tracks `episodeId`, `episodeTick`, episode length, and reset reason.
- Terminates episodes on timeout, loop, panic, or manual reset.
- Starts the next episode without wiping personality, habits, or long-term memory.

## RewardEngine
- Produces a stable breakdown every tick:
  - `homeostasis`
  - `explore`
  - `social`
  - `loopPenalty`
  - `invalidActionPenalty`
  - `total`
- `RewardCalculator` is the stable entry point used by `LifeLoop`.

## LoopDetector
- Detects repeated fingerprints and alternating `ABAB` patterns over a bounded window.
- Exposes loop strength, streak, loop type, and aggregate counters.
- Feeds penalties into reward and episode termination.

## Scenario and Curriculum
- `CurriculumManager` owns the active scenario and progression mode.
- Scenarios parameterize world climate and event probabilities without changing core logic.
- Curriculum modes decide when to switch scenarios.

## ML Integration
- `IMlPolicyAdvisor` isolates the brain from ML implementation details.
- Backends can be `stub`, `local`, or `remote`.
- Action masks are enforced before and after ML recommendations.
- Evaluation mode disables training and keeps inference-only telemetry.

## Telemetry
- `LifeStateDto` is the stable telemetry contract for Studio.
- `Trace` carries compact technical lines plus structured stage data.
- `Output` carries brain-facing messages only.
- `Logs` carry system events, configuration, and transport diagnostics.

## Reports
- Finished episodes are serialized as JSONL to `reports/episodes/YYYY-MM-DD/episodes.jsonl`.
- `PolicyEvaluator` aggregates episode quality over a rolling window for training and evaluation views.
