# Goal Journal — bones-v3-learning

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---


### OBS-001: Checked-in defaults are bounded, but the cap can be overridden to 0

- **Source**: `WIP/Wip.Bones.Host/appsettings.json:1-60`; `bones.ps1:22-26`
- **Fact**: The shipped configuration uses MaxGamesPerRun=200, one session, one observe game, 8 evaluation matches and cross-seat 1. `bones.ps1` throws only when it is the launcher and sees `BONES_MAX_GAMES_PER_RUN=0` without `BONES_ALLOW_UNBOUNDED=true`.
- **Implications**: Direct host start or a different launcher bypasses the only refusal path.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-001, EVD-002

### OBS-002: The host starts unbounded without an opt-in

- **Source**: round-1 host probe command and `/api/bones/status`
- **Fact**: With `BONES_MAX_GAMES_PER_RUN=0` and no `BONES_ALLOW_UNBOUNDED`, the host logged a warning, stayed alive, and reached iteration 5 in four seconds.
- **Implications**: The "unbounded requires explicit opt-in" acceptance criterion is not met.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-002, RISK-001

### OBS-003: Direct script execution has no deadline

- **Source**: `WIP/Wip.Bones/Engine/BonesStrategyScriptHost.cs:114-121`; `WIP/Wip.Bones/Engine/BonesMatchSimulator.cs:71-119`
- **Fact**: The direct path calls `ChooseMove` inline; the match loop has no elapsed-time check or process boundary. The timeout-guarded `ExecuteChooseMove` exists but is not used by match paths.
- **Implications**: An infinite-loop generated script can block the calling worker indefinitely.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-003, HYP-001

### OBS-004: Arena reproducibility holds on the recorded seed

- **Source**: repeated arena command; comparison of `dev2000.json` and `dev2000-round1.json`
- **Fact**: All 65 strategies had identical wins and mean score differential across two separate processes.
- **Implications**: The seed fix is effective for the game path; benchmark claims are reproducible.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-004

### OBS-005: Gate opponents and gate truth come from the same arena

- **Source**: `WIP/Wip.Bones.Benchmark/Program.cs:145-154`
- **Fact**: `opponents` is built from `arena.Strategies` and `strengths` is the same dictionary; the `gate` command takes one `--arena` path.
- **Implications**: Running the final gate against the held-out arena also tunes the opponent field on held-out data. The reported gate delta is therefore not a clean held-out estimate.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-005, HYP-004

### OBS-006: The full test suite is load-sensitive

- **Source**: `.tmp/round1-bones-tests.log`; single-test rerun
- **Fact**: Full suite: 367 passed / 3 failed, including the 100 ms sub-agent timeout test; the same test passes alone.
- **Implications**: Test counts are not a stable gate; the timeout test needs a robust threshold or isolation.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-006, RISK-002

### HYP-001: An infinite-loop script can hang a match indefinitely

- **Claim**: A script whose `ChooseMove` never returns will block the calling match worker forever in both `RunMatch` and the play tool.
- **Supporting OBS**: OBS-003
- **Why it matters**: This is a host-availability defect for model-authored code.
- **Falsification strategy**: Build a test that calls the direct path with an infinite-loop slot and a deadline; observe whether the call returns or throws within a bound.
- **Status**: Proposed
- **Confidence**: High

### HYP-002: Unbounded mode is not refused by the host

- **Claim**: Starting the host with MaxGamesPerRun=0 and no opt-in runs iterations.
- **Supporting OBS**: OBS-002
- **Why it matters**: The safety acceptance criterion fails.
- **Falsification strategy**: Start the host with cap 0 and observe status/iterations.
- **Status**: Under review
- **Confidence**: High

### HYP-003: The frozen benchmark is reproducible

- **Claim**: The same seed/config produces an identical arena.
- **Supporting OBS**: OBS-004
- **Why it matters**: A/B validity.
- **Falsification strategy**: Repeat the arena in a new process and diff all metrics.
- **Status**: Supported
- **Confidence**: High

### HYP-004: The gate study's held-out result is contaminated by dev/held-out leakage

- **Claim**: The gate's opponent field is selected from the same arena used as truth.
- **Supporting OBS**: OBS-005
- **Why it matters**: The claimed +0.042 held-out effect may be optimistic.
- **Falsification strategy**: Inspect `Program.cs` argument flow; run gate with a dev arena and compare the opponent list.
- **Status**: Under review
- **Confidence**: High

### HYP-005: The reported test count is stable

- **Claim**: `Wip.Bones.Tests` always yields 368/370 with exactly two gate failures.
- **Supporting OBS**: OBS-006
- **Why it matters**: A flaky suite cannot be used as a gate.
- **Falsification strategy**: Repeat the full suite and the 100 ms timeout test under load.
- **Status**: Falsified
- **Confidence**: High

### RISK-001: Unbounded host can spend without limit

- **Risk**: A direct `dotnet run` with cap 0 bypasses the launcher guard and runs indefinitely.
- **Likelihood**: High
- **Impact**: High
- **Evidence**: EVD-002
- **Mitigation**: Refuse startup when cap=0 unless an explicit option/env opt-in is present; keep the launcher guard.
- **Gate**: G2

### RISK-002: Flaky timeout test corrupts the regression signal

- **Risk**: The 100 ms sub-agent timeout test fails under parallel load, making the suite non-deterministic.
- **Likelihood**: High
- **Impact**: Medium
- **Evidence**: EVD-006
- **Mitigation**: Use a less aggressive timeout (or isolate the test) without weakening the fallback assertion.
- **Gate**: G2

### OQ-001: Does a match-level watchdog satisfy the infinite-loop safety requirement?

- **Question**: Can a watchdog thread/task fail the match within a configured deadline while keeping the host responsive, without process isolation?
- **Needed evidence**: A test that runs an infinite-loop slot, observes the deadline exception and a still-responsive process.
- **Priority**: P0 blocking
- **Raised by**: Round 1, orchestrator
- **Related**: HYP-001, EVD-003

### OQ-002: What is the clean held-out gate delta when opponents are chosen from dev?

- **Question**: Does selecting the per-seat opponent field from the dev arena change the confidence-vs-OR delta?
- **Needed evidence**: Rerun the gate with `--opponents-arena dev2000.json` and compare.
- **Priority**: P1 important
- **Raised by**: Round 1, orchestrator
- **Related**: HYP-004, EVD-005

### VAL-001: Round 1 claim batch (claims 1, 2, 5)

- **Target**: round-1 claim batch (claims 1, 2, 5)
- **Method**: Two independent Falsifiers ran the host, benchmark, and test suite; the orchestrator independently reproduced the unbounded host start and the arena repeat. The Synthesizer merged the tables under the consensus rule.
- **Evidence examined**: `docs/goal-bones-v3-learning/round-1/falsify-a.md`, `falsify-b.md`, `verdicts.md`; `WIP/Wip.Bones.Host/bin/Debug/net10.0/Wip.Bones.Host.exe` probe; `Wip.Bones.Benchmark.exe arena` repeat
- **Result**: Falsified — claim 1 (unbounded start reachable), claim 2 (no match deadline/process boundary), claim 5 (gate opponents and truth share the held-out arena). Claims 3 and 4 Supported.
- **Conclusion**: Three concrete defects are established. The benchmark numbers and the 368/370 suite count reproduce, but the gate result is not a clean held-out estimate and the host can start unbounded.
- **Related**: EVD-002, EVD-003, EVD-005, HYP-001, HYP-002, HYP-004

### VAL-002: Reported test count is not stable under load

- **Target**: HYP-005
- **Method**: Full suite run once, then the 100 ms timeout test alone; compare.
- **Evidence examined**: `.tmp/round1-bones-tests.log` (367/3/370, including `DelegateMoveDecision_GivenSubAgentTimeout_FallsBackToHeuristic`); single-test run passed.
- **Result**: Falsified — the suite can report a third failure under load.
- **Conclusion**: The 368/370 count is the normal result but is not a deterministic gate; the 100 ms timeout test needs stabilisation.
- **Related**: EVD-006, RISK-002, HYP-005

### DEC-001: Round 2 and 3 targets

- **Decision**: Round 2 fixes the unbounded-host refusal (claim 1) and separates the gate opponent arena from the truth arena (claim 5). Round 3 fixes the match deadline (claim 2). Round 4 re-tests all claims and runs the coverage review.
- **Rationale**: Claims 1 and 5 are small, independent changes with clear verification commands; claim 2 is a larger engine change and should not share a dispatch with them.
- **Alternatives considered**: Fixing all three in one implementer dispatch (rejected: overlapping host-options/DI files and too much scope for one verification round); leaving claim 5 as a documentation limitation (rejected: it invalidates the held-out claim).
- **Related**: VAL-001, RISK-003

### RISK-003: Round 1 gate G2 failed

- **Risk**: Three claims were falsified in round 1; the harness cannot mark any of them supported until the fixes are implemented and re-tested.
- **Likelihood**: Certain (observed)
- **Impact**: High — the goal's safety/measurement criteria are unmet
- **Evidence**: `round-1/verdicts.md`; `Falsified: 3 rows - 1, 2, 5`
- **Mitigation**: Re-dispatch corrected objectives in rounds 2-3 and re-run the claims; do not declare convergence until the re-test tables are Supported.
- **Gate**: G2

### VAL-003: Round 2 corrected claims (unbounded refusal and split gate arena)

- **Target**: corrected claims for round 1 #1 and #5
- **Method**: Implementer test-first changes, then orchestrator reran the host, launcher, gate commands, and focused suites.
- **Evidence examined**: `.tmp/goal-round2/verify-host.log`, launcher exit 1, `.tmp/goal-round2/verify-gate-clean.json`, `.tmp/goal-round2/verify-gate-absent.json`, focused host 126/126.
- **Result**: Supported for the corrected claims: host/launcher refuse effective zero without opt-in; split gate uses dev opponents + held-out truth and prints dev maxima; absent option unchanged.
- **Conclusion**: Round 1 defects #1 and #5 are fixed. #2 (match deadline) remains open for round 3.
- **Related**: EVD-007..EVD-011, HYP-002, HYP-004

### OQ-003: Stale co-learning restart test

- **Question**: The restart test expects a library entry after one run, but its mock enhancement returns Markdown and the enhance agent requires a script. Is this test obsolete, or should the library persist an active script independently of promotion?
- **Needed evidence**: Decide whether library bootstrapping should upsert the active Ponder/bootstrapped script; if yes, implement and update the test.
- **Priority**: P1 important
- **Raised by**: Round 2, orchestrator
- **Related**: EVD-012, RISK-002

### VAL-004: Round 2 corrected claims

- **Target**: corrected round-1 claims 1 and 5, plus regression checks
- **Method**: Orchestrator reran the host, launcher, split gate, absent-option gate, and focused host suite after the round-2 implementation.
- **Evidence examined**: `.tmp/goal-round2/verify-host.log`, launcher exit 1, `.tmp/goal-round2/verify-gate-clean.json`, `.tmp/goal-round2/verify-gate-absent.json`, focused host 126/126.
- **Result**: Supported 6/6. No Falsified rows in the corrected batch.
- **Conclusion**: Round 1 defects #1 and #5 are fixed and verified. The round-1 claim 2 (match deadline) remains the round-3 objective.
- **Related**: EVD-007..EVD-011, round-2/verdicts.md

### RISK-004: Round 2 falsifier agents exceeded the round budget

- **Risk**: Both independent round-2 falsifiers were stopped without artifacts; the round relies on orchestrator-run commands for verification.
- **Likelihood**: Observed
- **Impact**: Medium — independence is weaker for this round
- **Evidence**: `interrupt_agent` accepted for both ids; no `falsify-c/d.md` files
- **Mitigation**: The orchestrator ran every command; round 4 will re-run the final claim table with a fresh falsifier on the two highest-risk claims.
- **Gate**: G5

### HYP-006: Direct script moves are deadline-bounded

- **Claim**: With `EnableDirectExecutionTimeout=true`, a non-returning `ChooseMove` throws `BonesStrategyScriptExecutionTimeoutException` within `ExecutionTimeout` instead of blocking the caller indefinitely.
- **Supporting OBS**: EVD-013, EVD-014
- **Why it matters**: The play/evaluation path uses the direct slot; this is the only in-process safety net until a worker process exists.
- **Falsification strategy**: Run an infinite-loop compiled script through `ExecuteChooseMoveDirect` with a 200 ms timeout and observe a bounded return; inspect the disabled branch for behavioral change.
- **Status**: Under review (pending round-3 falsifier)
- **Confidence**: High

### RISK-005: Timed-out script thread is abandoned, not killed

- **Risk**: A timed-out infinite loop continues to consume one CPU core as a background thread until the host process exits; repeated timeouts could accumulate runaway threads.
- **Likelihood**: Medium (only on malicious/broken scripts)
- **Impact**: Medium (CPU, not blocking)
- **Evidence**: EVD-017
- **Mitigation**: Process-isolated worker with a kill handle (v3 roadmap M2); count timeouts and stop the run after a threshold.
- **Gate**: —

### VAL-005: Round 3 corrected claim (direct execution deadline)

- **Target**: corrected round-1 claim 2
- **Method**: Orchestrator ran the new direct-timeout tests, existing script-host tests, inspected the implementation, and ran a bounded host with the watchdog enabled.
- **Evidence examined**: `WIP/Wip.Bones/Engine/BonesStrategyScriptHost.cs:118-160`; `BonesStrategyScriptHostDirectTimeoutTests` (9/9 with existing tests); host run 20/20 cap; appsettings binding.
- **Result**: Supported 5/5, 0 Falsified.
- **Conclusion**: The caller is deadline-bounded. The timed-out thread remains alive in-process; process isolation is still required.
- **Related**: EVD-013..EVD-017, RISK-005

### OQ-004: Host promotion integration test does not promote under the new gate fixture

- **Question**: Why does `BonesLearningLoop_GivenStrongCandidateEnhancement` still read `initial-seat-1-v1` after the fixture was moved to 8 matches and `GateMode=LegacyOr`, while `FindPromotionSeed` finds a promoting seed in isolation?
- **Needed evidence**: A host-run trace of the promotion metrics/seed and the actual opponent artifacts, or a test-only forcing mechanism that does not weaken the product gate.
- **Priority**: P1 important
- **Raised by**: Round 4, orchestrator
- **Related**: OQ-003, D4

### OQ-005: Reliability compliance gate executes stale host tests

- **Question**: The reliability gate now passes checklist binding but fails while executing mapped planned tests that use Markdown enhancement mocks; should those tests be migrated to script responses, or should the plan stop claiming them?
- **Needed evidence**: Update the stale host mocks to script responses and rerun the gate, or move the restart/promotion scenarios to the v3 roadmap with an explicit OQ.
- **Priority**: P1 important
- **Raised by**: Round 4, orchestrator
- **Related**: EVD-012, OQ-003, D4

### RISK-006: Round 3 falsifier agent exceeded the budget

- **Risk**: The independent round-3 falsifier was interrupted without an artifact; the round relies on orchestrator-run tests.
- **Likelihood**: Observed
- **Impact**: Medium — independence weaker
- **Evidence**: `interrupt_agent` accepted; no `round-3/falsify.md`
- **Mitigation**: Orchestrator reran every command; round-4 coverage review records the gap.
- **Gate**: G5

### DEC-002: Stop the round loop at three verification rounds

- **Decision**: Stop after round 3 and report D4 as a concrete blocker rather than dispatch a fourth implementer for the stale host promotion test.
- **Rationale**: The blocking conditions are named with evidence (OQ-004, OQ-005); the remaining work is test-fixture/roadmap debt, not a safety or measurement defect in the changed code. Continuing would repeat the same stale-mock failure.
- **Alternatives considered**: Dispatch another implementer to rewrite the host promotion fixture (rejected: the product gate is already covered by focused evaluator tests and the strong test's own helper; the host test needs a design decision about the gate fixture). Process isolation (rejected: too large for this pass; recorded as M2).
- **Related**: OQ-004, OQ-005, RISK-006
