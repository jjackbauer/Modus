# Bones Learning Loop — v2 P0 Remediation

> Implemented: 2026-10-31
> Scope: the P0 runtime/control-plane fixes from [Live Evaluation and v2 Proposal](Bones%20Learning%20Loop%20%E2%80%94%20Live%20Evaluation%20and%20v2%20Proposal.md)
> Model used for live validation: `deepseek-flash` (V4.1 Flash, thinking disabled)

## 1. What was fixed

### 1.1 Hard game budget

`BonesGameSimulationBudget` is now a reservation ledger:

- `TryReserve(n)` atomically reserves capacity without incrementing `GamesSimulated`.
- `RecordGames(n)` settles actual games and releases matching reservations.
- `ReleaseReservation(n)` restores capacity when a stage fails or is cancelled.
- `RemainingGames`, `ReservedGames`, and `IsExhausted` expose the live state.

Every game-producing path now reserves before it runs:

- `BonesObserveGamesTool`: reserves `GameCount`.
- `BonesPlayMatchTool`: reserves one game per match.
- `BonesStrategyPromotionEvaluator`: reserves the full evaluation batch, adaptive batches, and cross-seat matches.

`BonesLearningLoopHost` stops at the budget boundary and sets:

- `capReached = true`
- `stopReason = "GameBudgetExhausted"`
- a structured `CapReached` iteration status

A cap smaller than the next stage's requirement now stops **before** that stage instead of overshooting.

### 1.2 Separate evaluation target score

Promotion evaluation no longer reuses the learning target score.

- New `BonesLearningWorkflowParameters.EvaluationTargetScore`.
- New host setting `BonesHost:EvaluationTargetScore` (default `12`).
- Task-description round-trip includes `evaluationTargetScore`.
- `BonesLearningWorkflowMapRuntime` maps the explicit evaluation target to `BonesEnhanceStrategyRequest`.
- Status exposes `perStageModels.evaluationTargetScore`.

Evaluation matches at target 12 replace 120-point evaluation games.

### 1.3 Bounded evaluation concurrency

- New `BonesStrategyPromotionOptions.MaxConcurrentMatches` (default `8`).
- Full and adaptive evaluation paths use a semaphore per evaluation; they no longer launch every candidate/incumbent match at once.
- Cross-seat evaluation also checks the budget per match.

### 1.4 Direct script execution in match paths

The timeout-guarded `Task.Run + Wait` path remains available for direct callers and its existing test, but match execution uses a direct path:

- New `BonesScriptExecutionMode.Direct`.
- `BonesStrategyScriptHost.ExecuteChooseMoveDirect` calls the script synchronously on the match worker.
- `BonesStrategyPlayerSlotFactory`, `BonesPlayMatchTool`, and `PopulationMatchComposer` create direct-mode slots.

This removes the nested task-per-move pattern that caused thread-pool starvation during evaluation.

### 1.5 Failure policy

New host settings:

- `MaxConsecutiveIterationFailures` (default `5`)
- `FailureBackoffBaseMs` (default `1000`)
- `FailureBackoffMaxMs` (default `60000`)

The loop now:

- tracks consecutive failed iterations,
- applies exponential backoff with a cap,
- stops with `stopReason = "ConsecutiveIterationFailures(n)"` after the threshold,
- resets the counter on success.

### 1.6 Seat coverage and pool invariants

- `BonesPlayMatchTool` fills any missing seat from the active strategy store, then a deterministic seed fallback, before starting a match.
- `StrategyPool.EvaluateAndPromote` never prunes below four entries (seat coverage).
- `StrategyPool.Prune` never prunes below seat coverage.
- This fixes the `KeyNotFoundException: key '1'` failure after a promotion.

### 1.7 Artifact read/write race

Status polling and other readers opened artifact files with the default `FileShare.Read`, which blocked atomic replace and produced:

> `UnauthorizedAccessException: Access to the path is denied.`

Artifact descriptor/content reads and strategy-library seat/candidate reads now use:

~~~csharp
FileShare.ReadWrite | FileShare.Delete
~~~

Promotion writes no longer fail just because the status endpoint is reading the same artifact or library entry. This removed the live `UnauthorizedAccessException` during promotion and the status 500s caused by concurrent library reads.

### 1.8 Status additions

`GET /api/bones/status` now includes:

- `gamesReserved`
- `gamesRemaining`
- `capReached` (now actually set)
- `stopReason`
- `perStageModels.evaluationTargetScore`

## 2. Live validation

All runs used `deepseek-flash`, thinking disabled, and a fresh data directory.

| Run | Configuration | Result |
|---|---|---|
| Hard cap smoke | `MaxGamesPerRun=3`, target 12, evaluation 1 | Stopped at 2 games with `capReached=true`, `stopReason=GameBudgetExhausted`; no evaluation overshoot |
| Full iteration | `MaxGamesPerRun=6`, target 12, evaluation 1×2 | Full observe/play/evaluate/enhance/promote iteration completed in ~91 s, 6 games, 1 promotion, `capReached=true` |
| Multi-seat | `MaxGamesPerRun=8`, 4 seats, evaluation 1 | All four seat evaluations ran, 8 games, no `KeyNotFoundException`, clean budget stop |
| Four-seat promotion | `MaxGamesPerRun=10`, 4 seats, evaluation 1, forced promotion | **3 promoted / 1 rejected**, 10 games, ~94 s, clean cap stop; no errors |
| Resume + pool path | Same data directory, `AllSeatsLearning=false`, `ResumeFromBest=true`, cap 6 | Pool loaded 100 entries, two iterations, 6 games, no `KeyNotFoundException`, clean cap stop |

Before the fix, one seat's 30-match evaluation took ~6 m 23 s and a `MaxGamesPerRun=2` run reached 1,326 games. After the fix, the same control plane stops at the configured boundary.

One observability defect remains visible in the first promotion run: status reported `poolSize=0` while the co-learning log reported pool sizes 1→4. The pool itself accepted the promoted entries; the DI/static status view is still inconsistent. This is tracked below.

## 3. Tests

New/updated coverage:

- Budget reservation, release, and remaining-capacity tests.
- Host cap integration with `capReached`/stop reason.
- Evaluation-target binding and invalid-value tests.
- Pool seat-coverage tests for `EvaluateAndPromote` and `Prune`.
- Script execution mode retains the existing timeout-guard test.

Validated:

- `Wip.Bones.Tests`: targeted budget/script/workflow tests pass.
- `Wip.Bones.Host.Tests`: budget, pool, options, cap, status, and DeepSeek adapter tests pass.
- `Wip.Artifacts.Local.Tests`: pass.
- Full host build: succeeds.

The full `Wip.Bones.Tests` suite remains 357/360 because of three pre-existing behavior-proof compliance gates whose requirement assumptions do not satisfy the repository's anchor rules. The full host suite also contains older integration tests with stale stage predicates; the focused control-plane suites above are green.

## 4. Still open (P1/P2)

Not part of this P0 patch:

- `currentStage` should expose an explicit `Evaluating` stage and evaluation progress (completed/total), not remain on `Play`.
- Status `poolSize` can disagree with the pool the co-learning agent mutates (0 vs 4 in the four-seat promotion run); remove the static `StrategyPool.Instance`/DI dual view.
- `gamesPerHour`, `averageGameDurationMs`, and `concurrentUtilizationPercent` still need production recording.
- Raw model response/compiler diagnostics should be persisted on parse/compile failures.
- Deterministic seed derivation (replace `HashCode.Combine`/string hash).
- Process-isolated script sandbox.
- Artifact retention/compaction.
- Composite/sequential promotion, multi-match enhancement context, lineage/rejection memory.
