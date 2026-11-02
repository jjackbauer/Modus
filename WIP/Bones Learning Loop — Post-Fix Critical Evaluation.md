# Bones Learning Loop — Post-Fix Critical Evaluation

> Evaluation date: 2026-10-31
> Basis: source review plus live runs after the P0 remediation
> Related: [v2 Proposal](Bones%20Learning%20Loop%20%E2%80%94%20Live%20Evaluation%20and%20v2%20Proposal.md) · [P0 Remediation](Bones%20Learning%20Loop%20%E2%80%94%20v2%20P0%20Remediation.md)

## 1. Verdict

The P0 work fixed the immediate runaway: the live loop now respects a hard game budget, stops on a failure streak, avoids the post-promotion `KeyNotFoundException`, and completes a four-seat promotion iteration in about 94 seconds instead of stalling for minutes.

The harness is still **not safe to run with its checked-in defaults, not reproducible, not fully observable, and not safe against a bad script**. The fixes made the control plane enforceable; they did not make the default configuration sane, the execution sandbox real, or the learning loop statistically meaningful.

This is a critical evaluation of what remains, ordered by risk.

---

## 2. Evidence from the post-fix runs

| Run | Result |
|---|---|
| Four-seat forced promotion, cap 10 | 10 games, **3 promoted / 1 rejected**, ~94 s, `capReached=true`, `stopReason=GameBudgetExhausted`, no errors |
| Resume from promoted library, `AllSeatsLearning=false`, cap 6 | pool loaded 100 entries, 2 iterations, 6 games, no `KeyNotFoundException`, clean cap stop |
| Invalid API key, failure threshold 2 | 2 iterations, 2 games, stopped with `ConsecutiveIterationFailures(2)`, last error 401 — no hot retry loop |
| Status during phase 1 | `poolSize=0` while co-learning logs reported pool sizes 1→4 |
| Status during evaluation | stage remained `Play` while evaluation matches ran |

These are real improvements. They also expose the next layer of defects.

---

## 3. Scorecard

| Area | Current state | Severity |
|---|---|---|
| Default run configuration | Unlimited cap, 16 sessions, 16 observe games, 30 evaluation matches/seat | **Critical** |
| Script execution safety | Direct path has no timeout; in-process Roslyn; a hostile/infinite script can hang the host | **Critical** |
| Reproducibility | Seeds use randomized `HashCode.Combine` and string hash codes | **Critical** |
| Status truthfulness | Pool/stage/throughput metrics disagree with runtime | High |
| Model-failure evidence | Raw response and compiler diagnostics are not persisted | High |
| Pool selection quality | Seat coverage is patched at match time; pool survivors are not seat-aware | High |
| Evaluation cost/shape | Seats evaluate sequentially; default 30 matches/seat remains expensive | High |
| Learning quality | AND-gate promotion, single-match enhancement, no lineage/rejection memory | High |
| Budget accounting edge cases | No reservation tokens; failed full evaluation can undercount completed games | Medium |
| Retention | No artifact/session retention; only the game cap limits growth | Medium |
| Test/behavior-proof debt | Full Bones suite is 357/360; full host suite has stale integration predicates | Medium |
| Documentation | README test counts and several historical claims are stale | Low |

---

## 4. Critical shortcomings

### 4.1 The checked-in default configuration is a cost trap

`appsettings.json` still ships with:

- `MaxGamesPerRun: 0` (unlimited)
- `ParallelSessionCount: 16`
- `DefaultParameters:GameCount: 16`
- `Promotion:EvaluationMatchCount: 30`
- `MinimumCrossSeatCount: 2`
- `AllSeatsLearning: true`
- `TargetScore: 120`

A single iteration at those defaults schedules, per session:

```text
16 observe + 1 play + 4 seats × (30 evaluation matches × 2 games) = 257 games
```

Across 16 sessions that is **4,112 games per iteration**, before cross-seat qualification. With `MaxGamesPerRun=0`, nothing stops it. The cap is now enforced when configured, but the shipped default is effectively "unbounded and expensive".

This is the most dangerous remaining defect because `bones.ps1 run` uses these defaults.

**Recommended fix:**

- Ship a safe default cap, for example `MaxGamesPerRun=200`, `ParallelSessionCount=1`, `GameCount=1`, `EvaluationMatchCount=4`, `MinimumCrossSeatCount=1`.
- Make unlimited mode explicit: require `BONES_ALLOW_UNBOUNDED=true` or a CLI flag.
- At startup, print the planned worst-case game count and estimated model calls. Refuse to start if the plan exceeds the cap by more than one iteration.
- Add a startup warning if `MaxGamesPerRun=0` or `ParallelSessionCount × per-session plan > 1000`.

### 4.2 Direct script execution has no timeout or sandbox

The P0 change removed the per-move `Task.Run(...).Wait(timeout)` from match paths. That fixed thread-pool starvation, but it also removed the only execution timeout:

- `BonesScriptExecutionMode.Direct` calls `BonesStrategyScriptHost.ExecuteChooseMoveDirect`.
- That method simply calls `compiled.PlayerSlot.ChooseMove(...)` with no timeout.
- `BonesMatchSimulator.RunMatchAsync` has no match-level timeout.
- Scripts still compile and run in the host process under a namespace string check.

A generated script containing an infinite loop, a huge allocation, or a blocking call will hang a match, and therefore the iteration, indefinitely. The old timeout path itself could not stop runaway code either; it only stopped waiting for it. So the system moved from "false timeout" to "no timeout".

This is a critical availability and safety defect for a self-building harness.

**Recommended fix:**

- Execute each match or script in a killable process boundary (separate worker with CPU/memory/wall-clock limits), not in the host process.
- If process isolation is too large for the next increment, add a **match-level watchdog** on a dedicated thread and fail the match when it exceeds a configured deadline; do not rely on `Task.Run` cancellation.
- Add a script contract that accepts a `CancellationToken` so cooperative scripts can be stopped.
- Add a test with an infinite-loop script proving the host survives and the iteration fails cleanly within the deadline.

### 4.3 Runs are still not reproducible

The P0 work did not change seed derivation. The loop still uses:

```csharp
var baseSeed = HashCode.Combine(
    _options.DefaultParameters.Seed,
    _sessionStore.HostSessionId.GetHashCode());
```

and evaluation uses `HashCode.Combine(request.BaseSeed, matchIndex, ...)`. `HashCode` is randomized per process, and `HostSessionId.GetHashCode()` is a randomized string hash. Two runs with identical configuration therefore produce different matches, different evaluation samples, and different promotion decisions.

This invalidates A/B comparisons, regression tests, and any claim that a strategy improved because of a code change rather than a new random sample.

**Recommended fix:** introduce a `SeedService` backed by a stable hash (SHA-256/FNV) of a persisted run id, iteration, seat, and match index. Put the run seed in status and every artifact.

### 4.4 Status and pool telemetry still disagree with runtime

Observed in the four-seat run:

- `/api/bones/status` reported `poolSize=0`.
- The co-learning agent logged `Pool size: 1`, then 2, 3, 4.
- A resume run reported `poolSize=100`.

The cause is the remaining dual ownership of `StrategyPool`: DI resolves one instance for the status endpoint, while `BonesCoLearningEnhanceAgent` falls back to `StrategyPool.Instance` when its optional dependency is not injected. The host still writes `StrategyPool.Instance` at startup. This is a correctness issue, not a display issue: the operator cannot tell which pool is being mutated or persisted.

Also still inaccurate:

- `currentStage` remains `Play` while evaluation runs; the stage monitor only recognizes `Ponder`, `Play`, and `Enhance`.
- `gamesPerHour`, `averageGameDurationMs`, and `concurrentUtilizationPercent` remain 0; `BonesCoLearningMetrics.RecordGameCompleted` has no production caller.
- `lastIterationError.stage` can still be inferred incorrectly from which artifact is missing; an enhance failure was previously labeled `Ponder`.
- Final `stopReason` is in-memory only; a restart loses why the prior run stopped.

**Recommended fix:**

- Delete `StrategyPool.Instance`; register one `IStrategyPool` and inject it into host, status, web routes, and agents.
- Replace artifact-name stage inference with explicit stage events emitted by `BonesLearningLoopHost`; include `Evaluate` and `Promote` with completed/total counters.
- Record game completion and utilization in the host loop or a run-state service.
- Persist run stop reason and seed in a run manifest.

### 4.5 Model failures still have no forensic evidence

The failure artifact type supports `ModelProviderResponseExcerpt`, but the host calls the overload that passes `null`. When an enhancement response is not compilable, the artifact records only the exception message, not:

- the raw model response,
- finish reason,
- token usage,
- model id/thinking mode,
- compiler diagnostics,
- prompt version.

The 401 failure experiment produced a clean error string, but a format/compile failure remains nearly impossible to diagnose from artifacts. The model gateway still has no structured-output validation or repair loop beyond the existing compile-retry prompt.

**Recommended fix:** make the model gateway return a result envelope containing raw content, finish reason, usage, and validation errors; persist it on every failure. Add a JSON schema/structured-output request and a bounded repair loop.

### 4.6 Pool selection is not seat-aware

`StrategyPool.EvaluateAndPromote` now keeps at least four entries, but it still selects survivors purely by composite score. It can keep four entries all authored for seat 1 and none for seat 2. The match-time fallback in `BonesPlayMatchTool` fills missing seats from the active strategy store or deterministic seeds, so the match no longer crashes — but the population pool is not a valid four-seat population.

This masks a quality problem: a pool that looks populated may be functionally unbalanced, and `PopulationMatchComposer` may deploy the wrong strategy identities.

**Recommended fix:** select survivors per seat (at least one per seat, then global fill), and assert seat coverage in `EvaluateAndPromote`, not only at match time. Expose per-seat pool counts in status.

### 4.7 Evaluation is still expensive and serial across seats

`BonesCoLearningEnhanceAgent` loops seats 1→4 sequentially. Each seat runs its own promotion evaluation; `MaxConcurrentMatches` bounds matches within a seat, but seats do not overlap. The default `EvaluationMatchCount=30` and the 4-seat loop mean evaluation still dominates runtime.

The current live runs used `EvaluationMatchCount=1`, so they do not prove the default path is acceptable. At 30 matches per seat, even target 12, a four-seat iteration is still a multi-minute operation before cross-seat qualification.

**Recommended fix:** evaluate seats with bounded parallelism (for example two seats at a time), add a per-iteration evaluation budget, and use sequential testing to stop clearly decided comparisons early.

### 4.8 Budget accounting still has edge cases

The ledger is a major improvement, but it has no per-reservation token:

- `TryReserve` adds to a global reserved counter; `RecordGames` subtracts from the same counter regardless of which reservation produced it.
- If a full evaluation launches, then `Task.WhenAll` throws after some matches complete, the `finally` releases the full reservation without recording the completed games. The games happened; the budget undercounts them.
- `ComputePlannedIterationGameBudget` remains dead code and still computes only `GameCount + 1 + EvaluationMatchCount × 2`; it does not include four seats or parallel sessions, so it would understate the default plan if wired in.
- There is no startup check that the cap is large enough for a minimal iteration; a very small cap silently stops after observe/play, which is safe but surprising.

**Recommended fix:** return a reservation handle from `TryReserve`, settle it with actual games, and release only the unused remainder. Record completed games before outcome persistence. Delete or correct the planned-budget method and add a startup feasibility check.

---

## 5. High-priority learning shortcomings (unchanged by P0)

These were documented in the companion adversarial analysis and remain the reason the harness is not yet a strong learning system:

- **AND-gate promotion:** a candidate must improve both win rate and score differential. The noisy win-rate threshold still rejects high-score candidates.
- **Single-match enhancement context:** the enhancer sees one match, so it cannot distinguish a heuristic flaw from tile variance.
- **No lineage or rejection memory:** rejected strategies are not fed back into Ponder/Enhance; the model can revisit dead ends.
- **Markdown dead zone:** seats without a compilable script keep re-Pondering from stale observations.
- **No opponent-aware enhancement:** the model does not see opponent strategies or profiles.
- **No process rewards:** score improvement, compilation success, and stability are not credited.

The P0 work makes these measurable; it does not fix them.

---

## 6. Other remaining shortcomings

- **Artifact/session retention is absent.** The cap bounds games, not sessions or artifacts. A failure loop below the threshold still creates sessions and files. Add retention/compaction by age/kind/size.
- **In-process Roslyn remains a safety risk** beyond the timeout issue: generated code can allocate memory, spawn threads, or access APIs that the namespace check misses.
- **Test debt.** Full `Wip.Bones.Tests` is 357/360, with three behavior-proof compliance gates failing on pre-existing assumption/test-plan mismatches. The full host suite still contains stale integration predicates that wait for stage names the host no longer emits. New P0 behavior is covered by focused tests, but the canonical gates are not green.
- **Documentation debt.** README test counts and historical claims are stale; the pool/status mismatch is not visible in the UI.
- **No cost telemetry.** Token usage and dollar cost are not aggregated per iteration/seat/stage, so an operator cannot set a cost budget.

---

## 7. What genuinely improved

To keep the evaluation balanced:

- Hard budget enforcement works: a cap of 10 stopped exactly at 10 games; a cap of 6 stopped at 6.
- Failure policy works: invalid credentials produced two attempts and a clean `ConsecutiveIterationFailures(2)` stop, not 600 retries.
- A four-seat promotion iteration now completes in ~94 seconds and promotes candidates.
- The post-promotion `KeyNotFoundException` is gone in the resume/pool run.
- Artifact and library read sharing removed the live `Access to the path is denied` failure.
- Focused control-plane suites are green.

---

## 8. Recommended next increments

| Increment | Scope | Acceptance |
|---|---|---|
| A. Safe-by-default | Cap defaults, startup plan/cost check, require explicit unbounded flag | `bones.ps1 run` cannot start an unbounded 4k-game plan accidentally; status prints planned vs cap |
| B. Execution safety | Match-level watchdog and/or process-isolated script runtime | An infinite-loop script fails the iteration within the deadline; host remains healthy |
| C. Determinism | Stable seed service + persisted run manifest | Two runs with the same seed produce identical transcripts, evaluation samples, and decisions |
| D. Observability truth | Remove `StrategyPool.Instance`; explicit `Evaluate`/`Promote` stages; record game/utilization metrics | Status pool count matches the mutated pool; evaluation progress is visible; metrics are non-zero |
| E. Failure forensics | Raw model response, usage, finish reason, compiler diagnostics in failure artifacts | A compile failure can be diagnosed without rerunning the model |
| F. Learning quality | Composite/sequential promotion, multi-match context, lineage/rejection memory | Candidate decisions use ≥N matches; prior rejections appear in the prompt |

If only one item can be done next, do **A**. The current defaults are the largest remaining operational risk; the harness is now good at stopping when told, but the checked-in configuration never tells it to stop.
