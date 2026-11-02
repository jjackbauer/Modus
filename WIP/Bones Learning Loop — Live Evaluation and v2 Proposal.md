# Bones Learning Loop — Live Evaluation and v2 Proposal

> Evidence run: 2026-10-31 15:52–16:46 America/Sao_Paulo
> Model: `deepseek-flash` (DeepSeek V4.1 Flash, thinking disabled)
> Scope: runtime, reliability, cost, observability, and learning quality
> Companion document: [Bones Learning Loop — Adversarial Analysis & Enhancement Opportunities](Bones%20Learning%20Loop%20%E2%80%94%20Adversarial%20Analysis%20%26%20Enhancement%20Opportunities.md) (promotion/enhancement critique)
> Implementation status: P0 control-plane items in this proposal were implemented on 2026-10-31. See [v2 P0 Remediation](Bones%20Learning%20Loop%20%E2%80%94%20v2%20P0%20Remediation.md).

---

## 1. Verdict

Bones is a real end-to-end self-building harness: it observes games, asks an LLM to author C# strategies, compiles them, plays matches, evaluates candidates, and updates a strategy library. The V4.1 Flash integration works: the cold-start run produced a compilable candidate, and the minimal controlled run completed an iteration with a promotion.

It is **not yet a safe self-building harness**. In the live run it ignored the configured game budget, spent over six minutes on one seat's promotion evaluation, then entered a 600+ iteration failure loop that created 633 sessions and 113 MB of artifacts in under an hour. The core loop is unbounded, the evaluation is misconfigured, the script runtime starves the thread pool, and the operational state is not trustworthy.

The shortest path to a useful v2 is not a larger model or a more elaborate prompt. It is to make the loop **bounded, observable, resumable, and statistically honest** before adding more learning machinery.

---

## 2. What was run

Two controlled experiments were run against the real DeepSeek API.

| | Experiment A | Experiment B |
|---|---|---|
| Intent | Realistic cold start | Minimal controlled iteration |
| Seats learning | 4 | 1 |
| Parallel sessions | 1 | 1 |
| Game count per session | 1 | 1 |
| Target score | 120 | 12 |
| Promotion evaluation matches | 30 | 1 |
| `MaxGamesPerRun` | 2 | 3 |
| Resume from best | false | false |
| Model / thinking | `deepseek-flash` / disabled | `deepseek-flash` / disabled |

Experiment A was the realistic path. Experiment B intentionally reduced target score and evaluation count so a full iteration could be observed in about a minute.

### 2.1 Experiment A evidence

| Observation | Value |
|---|---|
| Session created | 18:53:10 UTC |
| All four Ponder executions complete | 18:53:45 UTC |
| Learning match complete | 18:53:46 UTC |
| Learning match length | 12 rounds, **315 turns**, winner seat 4 |
| Seat-1 candidate script saved | 18:53:55 UTC |
| Seat-1 promotion evaluation complete | 19:00:18 UTC |
| Seat-1 evaluation wall time | **~6 m 23 s** for 30 candidate matches (plus incumbent matches) |
| Seat-1 evaluation result | 30 matches, 11 wins, 19 losses, cumulative score differential -1378 |
| Status at 19:00:30 | stage `Enhance`, iteration 0, games 62, `capReached=false` |
| Status at 19:42:25 | **iteration 632, games 1326, `capReached=false`** |
| Co-learning sessions completed | 1 |
| Strategies compiled (status) | 0 |
| Strategies promoted (status) | 1 |
| Last iteration error | `Co-learning enhance failed for all 4 seats. No enhancement results produced.` |
| Sessions / files / disk after 50 min | **633 sessions / 38,017 files / 113.52 MB** |

The loop was still running when stopped. `BONES_MAX_GAMES_PER_RUN=2` did not stop it. After the first slow iteration, it settled into a roughly four-second failure cycle: create session, observe, play two games, fail all four enhance attempts, repeat. It continued for 632 iterations.

### 2.2 Experiment B evidence

| Observation | Value |
|---|---|
| Iteration 1 | Ponder 16:44:36 → Play 16:45:11 → complete 16:45:39 |
| Iteration 1 games | 5 |
| Iteration 1 result | `sessions=1`, `promoted=1`, `capReached=false` |
| Iteration 2 | failed after ~6 s with `KeyNotFoundException: key '1'` |
| Iterations 3–7 | failed in 4–20 s with missing-seat or C# compile errors |
| Games at iteration 7 | 11, with `MaxGamesPerRun=3`, `capReached=false` |
| Status pool size | 0 throughout |

The first iteration proves the happy path works. The second iteration exposes a state-invariant bug: after one promotion, the pool-driven match composition can omit a seat, and `BonesPlayMatchTool` indexes a missing key at line 425.

---

## 3. Strengths

These are real assets and should be preserved in v2.

1. **Complete loop exists.** Observe → Ponder → Play → Enhance → Evaluate → Promote is wired and runs end to end.
2. **The model integration works.** V4.1 Flash generated a compilable C# strategy on the first cold-start attempt; the response parser, compiler, and artifact store all cooperated.
3. **Artifact-backed evidence.** Every stage emits inspectable artifacts, including full match transcripts and viewer URLs. This is the right foundation for replay and debugging.
4. **Script strategies, not only prompts.** The system compiles and executes model-authored `IBonesPlayerSlot` implementations, which is a meaningful step beyond markdown-only agents.
5. **Multi-seat co-learning and a strategy library.** Per-seat active/candidate strategies, ELO ratings, a population pool, and promotion artifacts already exist.
6. **Provider resilience.** The DeepSeek provider has retry, circuit-breaker, bulkhead, and rate-limit plumbing; status exposes circuit state.
7. **Configurability.** Model, thinking mode, temperature, target score, evaluation count, population size, and resilience are externally configurable.
8. **Test and documentation culture.** The repository has an unusually rich set of behavior-proof tests and requirement documents; the audit/evidence habit is a genuine advantage.

The problem is not missing ambition. It is that the safety and control plane around these parts is incomplete.

---

## 4. Critical weaknesses

### P0.1 — The game budget is not enforced anywhere

**Observed:** `BONES_MAX_GAMES_PER_RUN=2` produced 1326 games and 632 iterations with `capReached=false`. `BONES_MAX_GAMES_PER_RUN=3` produced 11 games in Experiment B, still with `capReached=false`.

**Code evidence:**

- `BonesLearningLoopHost.ExecuteAsync` starts a new iteration unconditionally (around lines 121–189). It never calls `_gameBudget.TryReserve` and never sets `_state.CapReached`.
- `BonesGameSimulationBudget.TryReserve` is a pure check and is not called from production host/tool code. Repository-wide grep finds callers only in tests.
- `BonesPlayMatchTool` and `BonesObserveGamesTool` call `RecordGames` **after** the game, never before.
- `BonesStrategyPromotionEvaluator` records `matchesCompleted * 2` after evaluation, never reserves the 60 matches before launching them.
- `BonesLearningLoopHost.ComputePlannedIterationGameBudget()` is dead code; only tests call it.
- `BonesLoopState.CapReached` has a setter and is read for the final status string, but no production code assigns it.

**Consequence:** there is no cost ceiling. A run cannot be safely bounded for CI, overnight experiments, or a paid API key. The README's claim that `MaxGamesPerRun` stops the loop gracefully is currently false.

**v2 requirement:** admission control must happen before an iteration's work is launched, and every game-producing subsystem must consume from the same budget.

---

### P0.2 — Promotion evaluation uses the learning target score (120), not a short evaluation target

**Observed:** the learning match and every promotion match were played to 120 points. A single match took 12 rounds / 315 turns. One seat's 30-match evaluation took ~6 m 23 s.

**Code evidence:**

- `BonesLearningWorkflowMapRuntime` sets `EvaluationTargetScore: parameters.TargetScore` (lines 81 and 88).
- `BonesEnhanceStrategyAgent` has a sensible `DefaultEvaluationTargetScore = 8` (line 23), but the workflow always supplies 120, so the default is never used.
- The evaluator launches `EvaluationMatchCount * 2` matches per seat; with 30 matches that is 60 matches per seat, 240 games across four seats.

**Consequence:** evaluation is roughly 10–15× more expensive than necessary. A target score of 8–12 is sufficient to compare heuristics; 120-point matches add variance and runtime without adding discriminative power.

**v2 requirement:** separate `LearningTargetScore` from `EvaluationTargetScore`; default evaluation to 8–12, cap it, and expose it in config.

---

### P0.3 — Every script move nests `Task.Run` inside a blocking `Wait`

**Observed:** the host process consumed only ~40 s of CPU over more than six minutes of wall time while one seat's evaluation ran. That seat launched 60 concurrent evaluation matches on a host with only ~56 threads, mostly waiting. Across the four sequential seat evaluations the iteration schedules 240 matches. This is thread-pool starvation, not model latency.

**Code evidence:**

- `BonesStrategyScriptHost.ExecuteChooseMove` creates a `Task.Run` for every `ChooseMove`, then blocks on `chooseMoveTask.Wait(ExecutionTimeout)` (lines 109–123).
- `BonesMatchSimulator.RunMatchAsync` wraps each synchronous match in another `Task.Run` (lines 17–21).
- `BonesStrategyPromotionEvaluator.EvaluateFullAsync` starts all `matchCount * 2` matches at once via `Task.WhenAll` (lines 367–389).
- With four seats evaluated sequentially, each seat still launches 60 matches × 4 player slots, each move spawning another task and blocking.

**Additional bug:** the timeout is ineffective. Cancelling the token does not stop the already-running user script. The task continues after `Wait` times out, leaking CPU and making the timeout a false guarantee.

**Consequence:** evaluation latency is dominated by scheduler churn. The system can appear hung for minutes while doing little useful work. Under heavier concurrency it can deadlock or time out unpredictably.

**v2 requirement:** run script moves directly on the match worker. Bound match concurrency explicitly. Enforce a **match-level** timeout, not a task-per-move timeout. If untrusted code must be interruptible, isolate it in a worker process with a kill handle.

---

### P0.4 — Evaluation fan-out is unbounded and seats run sequentially

**Observed:** seat 1 took ~6 minutes; seats 2–4 had not completed when the run was already 62 games in. The co-learning enhance agent loops seats 1→4 sequentially (`BonesCoLearningEnhanceAgent`, lines 45–102), and each seat launches a full evaluation.

**Consequence:** a four-seat iteration can take 20–25 minutes even when the model responds quickly. A four-seat iteration schedules 240 evaluation matches; currently one seat at a time (60 concurrent), and naive seat parallelism would make it 240 concurrent. The evaluation path has no throttle shared with the play stage.

**v2 requirement:** a single `MatchScheduler` with a configurable `MaxConcurrentMatches` (default `Environment.ProcessorCount`), used by play, observe, and evaluation. Evaluate seats with bounded parallelism (for example 2 at a time), not an unbounded fan-out and not a strict serial loop.

---

### P0.5 — Failures produce a hot retry loop, not a controlled stop

**Observed:** after the first slow iteration, the host produced 632 iterations in under an hour. Most failed in about four seconds: observe, play two games, fail all four enhance calls, repeat. There was no backoff, no consecutive-failure threshold, no circuit breaker at the workflow level, and no operator-visible stop reason beyond the last error string.

**Code evidence:**

- `BonesLearningLoopHost` catches exceptions and immediately continues; `IterationDelayMs=0` removes even the small delay (lines 176–189).
- The only circuit breaker is inside the HTTP provider; it does not stop a workflow that fails during parsing/compilation or because of a missing strategy.
- The failure artifact stores `ModelProviderResponseExcerpt: null`; the overload that can carry an excerpt is never used.
- Failure-stage inference marks an enhance failure as `Ponder` when the ponder artifact is absent (`BonesLearningLoopStageMonitor.InferFailureStage`, lines 86–104). The live failure artifact for an enhance failure literally says `"FailureStage":"Ponder"`.

**Consequence:** a systematic prompt/format/state bug becomes a paid request loop, creates thousands of artifacts, and hides the actual cause. The 632-iteration run completed one co-learning session; the rest was waste.

**v2 requirement:** track consecutive iteration failures; exponential backoff with jitter; pause or stop after a configurable threshold; persist the raw model response and compiler diagnostics; classify failures from the actual stage, not from missing artifacts; expose `failureStreak`, `nextRetryAt`, and `stopReason`.

---

### P0.6 — Active-strategy and pool invariants are not maintained

**Observed:** Experiment B iteration 2 failed with:

> `System.Collections.Generic.KeyNotFoundException: The given key '1' was not present in the dictionary.`
> at `BonesPlayMatchTool.RunAgentMatchAsync ... line 425` → `strategies[activePlayer]`.

**Code evidence:**

- With `AllSeatsLearning=false`, the play tool prefers pool-driven composition when `_strategyPool.Count >= 4` (`BonesPlayMatchTool`, lines 130–137). `ComposePoolDrivenStrategies` can return a dictionary without all four seats after the pool has been pruned or evaluated.
- `BonesPlayMatchTool` indexes `strategies[activePlayer]` without a fallback (line 429).
- `StrategyPool.EvaluateAndPromote` computes `maxSize = entries.Length - K`, where `K = max(poolSize / 4, 4)`. For a pool smaller than five, `maxSize` can be negative and the pool is cleared (`BonesStrategyPool.cs`, lines 279–323; `TournamentSelector.ComputeK`, lines 156–158).
- The host keeps a DI `StrategyPool` singleton and also writes `StrategyPool.Instance`; status reported `poolSize=0` while the persisted snapshot contained 100 entries and the log said "Initial pool snapshot saved: 100 entries". This is an instance/visibility inconsistency, not just a display issue.

**Consequence:** one successful promotion can make the next iteration unrunnable. The system then loops on the same missing-seat failure, which is exactly what Experiment B showed after iteration 1.

**v2 requirement:** enforce invariants before every play stage: every seat has an active strategy and every pool composition covers all four seats. If either invariant fails, fall back to the active-strategy library and record a diagnosable error. Never let `EvaluateAndPromote` reduce the pool below seat coverage. Remove the static singleton dual-write; inject one pool instance everywhere.

---

### P0.7 — Status and metrics report the wrong state

**Observed:**

- Stage stayed `Play` for more than five minutes while promotion evaluation ran; it only changed to `Enhance` after seat 1 completed.
- `gamesSimulated=2` while 60+ evaluation matches were in flight; it jumped to 62 only after seat 1 finished.
- `capReached` was false in both experiments despite exceeding the cap by 100×.
- `strategiesCompiled=0` while scripts were being compiled and promoted; `strategiesPromoted=1`.
- `gamesPerHour`, `averageGameDurationMs`, and `concurrentUtilizationPercent` were 0.
- `poolSize=0` with a 100-entry snapshot.
- `lastIterationError` said "Co-learning enhance failed"; the failure artifact said `Ponder`.

**Code evidence:**

- `BonesLearningLoopStageMonitor` only recognizes `Ponder`, `Play`, and `Enhance` from artifact names; it has no `Observe`, `Evaluate`, or `Promote` stages and never emits progress.
- `BonesCoLearningMetrics.RecordGameCompleted` is never called from production; `ConcurrentUtilizationPercent` is never assigned.
- `strategiesCompiled` reads the DI script host's counter; the play/evaluation path can construct its own `BonesStrategyScriptHost` when injection is absent.
- `capReached` is never set.

**Consequence:** operators cannot tell whether the system is working, stuck, or burning money. The status page is not a control surface.

**v2 requirement:** derive status from an explicit run-state machine with stage events and in-flight counters. Report `stage`, `stageProgress`, `gamesCompleted`, `gamesInFlight`, `gamesReserved`, `budgetRemaining`, `evaluation.completed/total`, `llm.requests/tokens/cost`, `failureStreak`, and `stopReason`.

---

### P1.1 — Learning quality is still limited by the promotion gate and enhancement context

The companion adversarial analysis already documents this in detail; the live run reinforced it:

- The promotion gate is an AND of win-rate and score-differential thresholds, with a 30-match sample that is still statistically weak for a four-player stochastic game.
- Enhancement is scoped to a single `MatchGameId`, so the LLM sees one match rather than a distribution.
- Markdown-only library entries force repeated Ponder cycles ("Ponder dead zone").
- There is no lineage, rejection memory, opponent profile, or cross-iteration memory.
- Raw model failures are not stored, so prompt/format regressions cannot be diagnosed.

These are learning-quality issues, not just reliability issues. v2 should fix the control plane first, then apply the companion document's Tier 1 changes (composite/sequential promotion, multi-match context, Markdown viability, rejection archaeology).

### P1.2 — Runs are not reproducible

`BonesLearningLoopHost` derives the base seed from:

~~~csharp
var baseSeed = HashCode.Combine(
    _options.DefaultParameters.Seed,
    _sessionStore.HostSessionId.GetHashCode());
~~~

`HostSessionId` is a GUID string, and .NET string hash codes are randomized per process. Two runs with identical configuration therefore produce different seeds, different matches, and different strategies. That makes experiments, regressions, and A/B comparisons unreliable.

**v2 requirement:** a `SeedService` with a stable hash (for example SHA-256 of a persisted run id and iteration index) and an explicit run seed in status/artifacts.

### P1.3 — Running model-generated C# in-process is a safety and operability risk

`BonesStrategyScriptHost` compiles and executes model-authored code in the host process. The namespace check is a good first step, but it is not a sandbox. Model-generated code can consume CPU, allocate unbounded memory, block threads, or reach APIs the checker does not anticipate. The per-move timeout does not stop runaway code.

**v2 requirement:** execute strategies in a separate worker process (or a WASM/DSL sandbox) with CPU, memory, wall-clock, and output limits; kill the worker on timeout. If in-process execution remains for trusted strategies, make it opt-in and remove the false timeout guarantee.

### P1.4 — Artifact growth is unbounded

113 MB / 38,017 files / 633 sessions in 50 minutes. At that rate, a day-long run would produce tens of gigabytes and hundreds of thousands of files. The artifact store retains every observation, transcript, execution, and failure.

**v2 requirement:** retention policy per artifact kind, compaction of completed sessions, sampling of successful observations, and a storage budget in the run controller.

---

## 5. Root-cause chain

The failures are not independent. The chain is:

1. **No admission control** → every iteration starts regardless of budget.
2. **Evaluation uses the full learning target** → each evaluation match is 10–15× longer than necessary.
3. **Unbounded evaluation fan-out × per-move `Task.Run`** → thread-pool starvation and multi-minute wall times.
4. **No failure backoff** → compile/model failures become a hot loop.
5. **No strategy/pool invariants** → one promotion can make the next iteration throw.
6. **Artifact-derived status** → operators see the last completed stage, not the real one.
7. **No raw response capture** → the actual model/compiler failure is invisible.
8. **Static/DI dual state** → status, pool, and script-host counters disagree.

Fixing 1–4 would have prevented the runaway. Fixing 5–8 would have made the failure obvious and recoverable.

---

## 6. Proposed Bones v2

### 6.1 Principles

1. **Bounded by construction.** Every run has game, time, token, cost, and storage budgets; admission control happens before work.
2. **Deterministic.** One persisted run seed drives every match, evaluation, and promotion comparison.
3. **Observable.** A single run-state machine emits stage/progress/cost events; status is a projection of that state.
4. **Resumable.** A failed stage can be retried or resumed without redoing successful stages.
5. **Statistically honest.** Promotion uses sequential/composite evidence, not a single noisy threshold.
6. **Memory-aware.** Lineage, rejected strategies, evaluation evidence, and opponent profiles are first-class inputs.
7. **Safe execution.** Model-authored code runs in a bounded, killable sandbox.
8. **Single composition root.** No static singletons or optional `new` fallbacks for stateful services.

### 6.2 Component model

~~~text
RunController
  ├─ BudgetLedger (games, wall time, tokens, cost, storage)
  ├─ SeedService (persisted run seed, stable derivation)
  ├─ FailurePolicy (backoff, circuit breaker, max consecutive failures)
  └─ StageExecutor
       ├─ ObserveStage
       ├─ PonderStage
       ├─ PlayStage
       ├─ EvaluateStage
       ├─ EnhanceStage
       └─ PromoteStage

MatchScheduler (bounded concurrency, same throttle for play/observe/evaluate)
  └─ MatchRunner (direct script execution; match-level timeout)

StrategyService
  ├─ ActiveStrategyStore (one active strategy per seat, invariant-checked)
  ├─ CandidateStore
  ├─ EvaluationEvidenceStore
  ├─ LineageStore
  ├─ RejectionGraveyard
  └─ PopulationPool (seat coverage invariant)

ScriptRuntime
  ├─ CompileCache
  ├─ SandboxedWorker (process/WASM; kill on timeout)
  └─ ResourceLimits

ModelGateway
  ├─ RawResponseStore
  ├─ StructuredOutputValidator
  ├─ Token/CostAccounting
  └─ Retry/RepairPolicy

Observability
  ├─ RunState (single source of truth)
  ├─ Status API v2
  ├─ Metrics endpoint
  └─ Structured logs
~~~

### 6.3 Budget model

The current `TryReserve` is a pure check, and `RecordGames` happens after the fact. Replace it with a ledger that atomically reserves and settles.

~~~csharp
public sealed class BudgetLedger
{
    public bool TryReserve(int games, BudgetKind kind) { ... } // atomic: committed + reserved + games <= max
    public void Settle(int reserved, int actual) { ... }       // convert reservation to actual
    public void Release(int reserved) { ... }                  // on cancellation/failure
    public BudgetSnapshot Snapshot() => ...;                    // committed, reserved, remaining, exhausted
}
~~~

The loop:

~~~csharp
var planned = ComputePlannedIterationGameBudget();
if (!ledger.TryReserve(planned, BudgetKind.Iteration))
{
    state.CapReached = true;
    state.StopReason = "GameBudgetExhausted";
    break;
}
try
{
    await RunIterationAsync(...);
    ledger.Settle(planned, actualGames);
}
catch
{
    ledger.Release(planned - actualGames);
    throw;
}
~~~

Every `ObserveGamesTool`, `PlayMatchTool`, and `PromotionEvaluator` must also reserve its own immediate batch so a single stage cannot overshoot the ledger. The iteration-level reservation is the admission gate; the stage-level reservation is the safety net.

### 6.4 Match scheduler and script runtime

Replace the per-move `Task.Run` + `Wait` with a direct call:

~~~csharp
// MatchRunner, already on a bounded worker
var move = slot.ChooseMove(state, legalMoves); // no Task.Run, no blocking Wait
~~~

Bound evaluation:

~~~csharp
await Parallel.ForEachAsync(
    seeds,
    new ParallelOptions { MaxDegreeOfParallelism = options.MaxConcurrentMatches, CancellationToken = ct },
    async (seed, token) => await RunMatchAsync(seed, token));
~~~

If a match timeout is required, apply it at the match level and run the match on a dedicated worker that can be abandoned/killed. Do not create a task per move.

The script runtime should expose either:

- a **cooperative** `ChooseMove(state, moves, CancellationToken)` contract, or
- a **process-isolated** worker with a hard kill.

The current in-process `Task.Run` timeout is neither cooperative nor enforceable.

### 6.5 Stage state machine

~~~text
Created -> Observing -> Pondering -> Playing -> Evaluating -> Enhancing -> Promoting -> Complete
                                                     -> Failed
Any -> Stopped | CapReached | Paused
~~~

Every transition emits a `StageEvent` with:

- iteration, session, seat, stage
- started/completed timestamps
- games reserved/settled
- model calls, tokens, cost
- progress counters (for evaluation: matches completed / total)
- failure classification and raw response reference

Status is a projection of the latest event plus counters. It should never infer the stage from "which artifact exists."

### 6.6 Evaluation design

1. **Separate targets:** `LearningTargetScore` (for the actual learning match) and `EvaluationTargetScore` (default 8–12, capped).
2. **Sequential testing:** start with a small batch (for example 4 candidate + 4 incumbent matches), stop early when the posterior/confidence clearly favors one side, continue up to a maximum.
3. **Composite score:** combine win rate, score differential, and stability; promote on a composite with a minimum regression guard, not a strict AND.
4. **Cross-seat only when cheap:** run cross-seat qualification after a provisional promote, not for every candidate.
5. **Bounded concurrency:** use the shared `MatchScheduler`; never launch `matches × 2` unbounded tasks.
6. **Evidence retention:** store the per-match results, not only the aggregate, so later analysis and lineage can use them.

### 6.7 Strategy and pool invariants

Before every Play/Evaluate:

~~~text
Assert one active strategy per seat 1..4
Assert pool composition covers seats 1..4
Assert candidate/incumbent are resolvable
Assert budget reservation exists for the stage
~~~

If an invariant fails:

- fall back to the last known active strategies,
- record a structured `InvariantViolation` artifact,
- do not start a new iteration until the invariant is repaired or the operator acknowledges.

`EvaluateAndPromote` must never clear below seat coverage. If `maxSize < 4`, keep all entries and skip pruning. Remove `StrategyPool.Instance` entirely; inject `IStrategyPool` into the host, status, tools, and viewer.

### 6.8 Memory and learning quality

Add a `StrategyMemory` store keyed by seat and lineage:

~~~text
lineage: strategyId -> parentId, diff summary, evaluation evidence, outcome
rejections: strategyId -> metrics, reason, prompt version
opponents: seat -> recent profiles and observed strategies
matches: sliding window of last N match transcripts
~~~

Load into Ponder/Enhance:

- prior strategy and diff,
- last N match transcripts (not one),
- evaluation evidence from the last promotion attempt,
- recent rejections and why,
- opponent profiles,
- prompt version and prior prompt outcomes.

This is the companion document's Tier 1/Tier 2 program, now with a reliable runtime to execute it.

### 6.9 Model gateway and prompt contract

1. **Structured output:** request a JSON object with `script`, `rationale`, `assumptions`; validate before compiling. Fall back to fenced-code extraction only for compatibility.
2. **Raw response capture:** on parse/compile failure, persist the full response, finish reason, token usage, and model id in the failure artifact.
3. **Repair loop:** send compiler diagnostics and a minimal diff back to the model; cap retries and count them.
4. **Token/cost accounting:** aggregate per stage, per seat, per iteration; expose in status and enforce a cost budget.
5. **Preflight:** a tiny startup chat completion checks key, model id, and thinking mode before the loop starts.
6. **Per-stage model/thinking:** allow Ponder/Enhance to use thinking, Play/Evaluate to use non-thinking, without global coupling.

### 6.10 Priority roadmap

| Priority | Change | Why | Acceptance |
|---|---|---|---|
| P0 | Enforce budget ledger in host and every game-producing stage | Stops runaway cost | `MaxGamesPerRun=10` stops at ≤10 + one iteration; `capReached=true`; no further LLM calls |
| P0 | Separate evaluation target score (8–12) | 10–15× cheaper evaluation | 60 evaluation matches complete in seconds at target 8; no 315-turn eval games |
| P0 | Remove per-move `Task.Run`/`Wait`; bound match concurrency | Eliminates thread-pool starvation | 240 matches with `MaxConcurrentMatches=16`; CPU utilization > 50%; no timeouts |
| P0 | Failure backoff + stop threshold | Stops hot failure loops | 3 consecutive failures → pause/stop; no more than 3 API attempts |
| P0 | Strategy/pool invariants + fallback | Prevents KeyNotFound loops | After a promotion, iteration 2 completes; status pool == snapshot |
| P0 | Status v2 with explicit stages/progress | Makes the system operable | `stage=Evaluating`, `evaluation.completed/total`, `gamesInFlight`, `budgetRemaining` |
| P1 | Raw response + compile diagnostics capture | Diagnoses model failures | Failure artifact contains response excerpt and compiler errors |
| P1 | Composite/sequential promotion; multi-match context; lineage | Improves learning signal | Candidate decisions use ≥N matches and prior evidence |
| P1 | Deterministic seed service | Reproducible experiments | Same run seed/config → identical transcripts and decisions |
| P1 | Sandboxed script runtime | Safety and killable timeouts | Malicious/infinite script killed without host impact |
| P1 | Retention/compaction | Bounded storage | 1-hour run stays under configured storage budget |
| P2 | Skill decomposition, prompt optimization, curriculum | Frontier capabilities | Separate skills can be authored/evaluated/recombined |

### 6.11 Experiments to run after v2

1. **Budget test:** cap 10, 4 seats, evaluation 4; assert stop at ≤10 games and no post-cap API calls.
2. **Evaluation throughput:** 4 seats × 8 evaluation matches at target 8; measure wall time, CPU, P99 match duration.
3. **Starvation regression:** run with `MaxConcurrentMatches=1,4,16,32`; assert throughput scales and no thread-pool timeouts.
4. **Failure policy:** inject a prompt/compile failure; assert backoff, stop threshold, and no artifact flood.
5. **Invariant test:** force a pool missing one seat; assert fallback and structured violation, not KeyNotFound.
6. **Determinism:** same run seed twice; compare transcripts, strategy ids, and promotion outcomes.
7. **Learning A/B:** baseline AND-gate/single-match vs composite/multi-match/lineage over N iterations; compare promotions and score differential.
8. **Cost:** measure tokens and dollars per promoted strategy before/after.

---

## 7. Expected impact

If P0 items are implemented:

- **Cost:** evaluation target 8 instead of 120 reduces moves per evaluation match by roughly 10–15×; bounded evaluation concurrency reduces wall time by another large factor; budget enforcement prevents the 1326-game overflow entirely.
- **Latency:** removing per-move `Task.Run` and evaluating with bounded parallelism should turn the 6 m 23 s seat evaluation into seconds, not minutes.
- **Reliability:** no 632-iteration failure loops; one promotion cannot break the next iteration; failures become visible and bounded.
- **Operability:** status becomes a control surface with stage, progress, budget, cost, and stop reason.
- **Learning:** with the runtime fixed, the companion document's promotion/enhancement improvements can actually be measured instead of being drowned by scheduler noise.

The current system is a promising prototype with real end-to-end capability. The v2 work is primarily control-plane engineering, not a rewrite: enforce budgets, bound concurrency, remove nested task blocking, maintain strategy invariants, and make state observable. Those changes are small relative to the learning architecture already present.

---

## Appendix A — Key code locations

| Concern | File | Lines |
|---|---|---|
| Loop starts iterations without budget check | `WIP/Wip.Bones.Host/BonesLearningLoopHost.cs` | 121–189 |
| Dead planned-budget method | `WIP/Wip.Bones.Host/BonesLearningLoopHost.cs` | 207–210 |
| Nondeterministic base seed | `WIP/Wip.Bones.Host/BonesLearningLoopHost.cs` | 134–137 |
| Budget check (unused by production) | `WIP/Wip.Bones.Agents/Budget/BonesGameSimulationBudget.cs` | 36–56 |
| Budget recording only | `WIP/Wip.Bones.Agents/Play/BonesPlayMatchTool.cs` | 231 |
| Observe recording only | `WIP/Wip.Bones.Agents/Observe/BonesObserveGamesTool.cs` | 118 |
| Evaluation records after the fact | `WIP/Wip.Bones.Agents/Knowledge/BonesStrategyPromotionEvaluator.cs` | 193, 207, 221, 421 |
| Unbounded evaluation fan-out | `WIP/Wip.Bones.Agents/Knowledge/BonesStrategyPromotionEvaluator.cs` | 367–389 |
| Per-move `Task.Run` + blocking wait | `WIP/Wip.Bones/Engine/BonesStrategyScriptHost.cs` | 109–123 |
| Match-level `Task.Run` | `WIP/Wip.Bones/Engine/BonesMatchSimulator.cs` | 17–21 |
| Evaluation target = learning target | `WIP/Wip.Bones.Agents/Workflow/BonesLearningWorkflowMapRuntime.cs` | 81, 88 |
| Sensible evaluation default unused | `WIP/Wip.Bones.Agents/Enhance/BonesEnhanceStrategyAgent.cs` | 23 |
| Sequential seat enhance | `WIP/Wip.Bones.Agents/Enhance/BonesCoLearningEnhanceAgent.cs` | 45–102 |
| Stage inferred from artifacts only | `WIP/Wip.Bones.Host/BonesLearningLoopStageMonitor.cs` | 20, 58–84 |
| Wrong failure-stage inference | `WIP/Wip.Bones.Host/BonesLearningLoopStageMonitor.cs` | 86–104 |
| `CapReached` never assigned in production | `WIP/Wip.Bones.Host/BonesLoopState.cs` | 46–50 |
| Metrics never recorded | `WIP/Wip.Bones.Host/BonesCoLearningMetrics.cs` | 97, 109 |
| Pool clear on small population | `WIP/Wip.Bones.Agents/Knowledge/BonesStrategyPool.cs` | 279–323 |
| `ComputeK` minimum 4 | `WIP/Wip.Bones.Agents/Knowledge/TournamentSelector.cs` | 156–158 |
| Missing-seat lookup | `WIP/Wip.Bones.Agents/Play/BonesPlayMatchTool.cs` | 425–434 |
| Static/DI pool assignment | `WIP/Wip.Bones.Host/BonesHostServiceCollectionExtensions.cs` | 150–218 |

## Appendix B — Raw evidence pointers

- Experiment A data directory: `.tmp/bones-v41-live`
- Experiment B data directory: `.tmp/bones-exp-min`
- Experiment A first session: `.tmp/bones-v41-live/.wip/sessions/9b5d8036a4e145d98828b35ec366fa9a`
- Experiment B failing session (iteration 2): `.tmp/bones-exp-min/.wip/sessions/6f053e72e92d4f55a9cd75d7dce86f36`
