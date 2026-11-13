# DeepBrain 1.8 — persistent intentions and temporary subgoals

Enable with `planning.enableIntentions=true` alongside the existing planner. The default remains false for old configurations. Production keeps the same memory paths, 38 ML inputs, eight actions, replay, weights and strategy learning. Web Studio 0.6 shows the main intention, active temporary goal, deferred progress, conditions for return and transition history in «Планы и опыт».

## Behavior

An urgent safety need, depleted energy, or a substantially higher-priority goal can suspend the current intention. A bounded chain retains ancestors, with the active goal as its leaf. Each goal type can occur only once, preventing cycles. Safety recovery retains priority while safety is below 50% or pain reaches 40%; energy recovery retains priority while energy is at most 35% or fatigue exceeds 75%. Critical safety preemption uses the existing 35% safety / 65% pain thresholds; depleted energy preempts cognitive tasks immediately at energy below 30% or fatigue above 75%.

After a temporary goal ends, the nearest relevant ancestor returns with the same plan ID, measured progress, strategy attempt, method phase and execution budget. Verification starts again. Paused time is excluded from plan age, stall timing and strategy attempt duration; energy spent on another goal is not charged to the parent attempt. Suspension is neither a goal failure nor a completed strategy attempt.

Return requires a current observation: matching scenario and episode, a still-relevant novelty/contact/need, and suitable safety and energy. A needed safety or energy prerequisite can run first. A failed prerequisite in cooldown cancels an unsuitable return instead of repeatedly executing an unsafe parent. Critical safety still bypasses cooldown. A needed recovery goal already in the ancestor chain is resumed by unwinding its children, never by adding a duplicate.

Expired, resolved or context-invalid intentions are recorded as interrupted with explicit reasons, without teaching their methods as failures. There are at most three deferred ancestors by default (`maxDeferredIntentions`, 1–4); exceeding the limit cancels the oldest. A deferred intention expires after 1800 awake ticks by default (`maxDeferredTicks`, 20–36000). Sleep does not advance this clock. This is persistence within the current simulated context, not arbitrary multi-day task scheduling: episode/context changes invalidate the associated plans.

## Persistence and compatibility

The optional `intentions` member is added to schema-1 `planning.json`. It has its own required schema, counts, ordered deferred plans and a bounded history of 40 transitions. Existing 1.7 files load without invented history; the active plan and learned attempts are retained. Full intentions, strategy attempts, learned models and outcomes commit in one atomic file replacement. Switching, resuming and cancellation trigger saving; failed writes retain the dirty in-memory state for retry.

Validation rejects missing required fields, invalid counters, duplicate goals/IDs, invalid timers, already completed active attempts and malformed strategy links before replacing the in-memory state. Disabling intentions explicitly cancels deferred plans while preserving the active leaf and recording why. Disabling strategy learning closes existing active/deferred attempts as interrupted, with unique attempt IDs retained for later re-enabling.

The core's body, RNG and complete partial-episode simulation are not serialized by this release. Fresh observations revalidate intentions after restart. Older runtimes ignore the additive hierarchy and can drop it when rewriting the file: before any downgrade, archive the latest full planning checkpoint. Never replace current memory/replay with an old backup merely to roll back binaries.

## Validation

Targeted tests cover migration, nested recovery, same-plan and same-attempt restoration, restart during a child and between child completion/parent resumption, sleep, measured verification, timer/energy isolation, stale context/contact/novelty cancellation, bounds, prerequisite failure, feature toggles, failed atomic writes and corrupted checkpoints.

A paired simulation runs the real LifeLoop in six scenarios with two seeds and 4000 ticks per run, comparing intentions disabled/enabled (24 runs). It checks bounded hierarchy, recorded returns to known IDs, saved/restored state and aggregate safety exposure. ML, LLM, habits and episodic memory are disabled to isolate this layer; results do not establish equivalent production performance or universal reductions in failed goals. The separate strategy-learning benchmark remains part of the regression suite.

Deployment backs up the complete production epoch and Web history before restart, verifies all 20000 replay entries and both learned networks immediately after loading, checks the active/deferred plan identities, counters and persisted attempts, and retains rollback copies. No training data is generated or injected into production for verification.
