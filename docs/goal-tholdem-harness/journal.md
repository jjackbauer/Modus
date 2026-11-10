# Goal Journal — tholdem-continual-harness

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---

<!-- New entries go below this line -->

### OBS-001: Requirements document exists and scopes the work

- **Source**: `harness/requirements/Wip.Tholdem.ContinualHarness.md:118-156`
- **Fact**: The requirements doc defines R-01 to R-12 plus the mandatory behavior-proof item, with T1 = R-01 to R-08 and T2 = R-09 to R-12.
- **Implications**: The build order is fixed: trajectory/signatures first, then memory/lineage/policy/bootstrap, then skills/ablation/meta-refiner.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: DEC-001

### DEC-001: Build the R-01/R-02/R-05 vertical slice first

- **Decision**: Round 1 implements trajectory capture, failure signatures and a minimal signature-driven refinement policy behind an opt-in `HarnessRefinement` flag, with tests and a legacy hash-equivalence check.
- **Rationale**: OBS-001 and the requirement dependency chain put R-01/R-02 before R-05; the experiment needs a refinement arm that differs from baseline, so R-05 must be in the first slice.
- **Alternatives considered**: Implementing memory/lineage/bootstrap first (rejected: no observable proposal difference); implementing only R-01/R-02 (rejected: no experiment).
- **Related**: OBS-001

### RISK-001: Concurrent sessions may edit the Tholdem tree

- **Risk**: Other untracked work in the workspace may change `Wip.Tholdem*` or shared solution files while this goal runs.
- **Likelihood**: Medium
- **Impact**: High
- **Evidence**: `git status` shows pre-existing modifications outside the Tholdem projects and other untracked goal directories.
- **Mitigation**: Restrict every dispatch to explicit paths; never `git add -A`; re-read `git status` before every commit-like action; keep all new product code under `Learning/Refinement` and new tests under `Learning`.
- **Gate**: —

### OBS-002: Round-1 refinement slice exists and is integrated opt-in

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/Refinement/*.cs`, `TrainingLoop.cs:118-164`, `TrainingOptions.cs:13`
- **Fact**: Five new refinement sources exist; `TrainingLoop` captures and extracts only when `HarnessRefinement` is true, otherwise calls the unchanged `MatchSimulator`/`StrategyProposer` path; the flag is the last positional `TrainingOptions` parameter with default `false`.
- **Implications**: R-01/R-02/R-05 have a working substrate; legacy mode remains the default and is independently hash-checked.
- **Confidence**: High
- **Agent**: implementer; verified by orchestrator
- **Related**: EVD-004, EVD-005, EVD-006, EVD-014

### OBS-003: Red-phase evidence exists for the new tests

- **Source**: `docs/goal-tholdem-harness/round-1/implementation.md:40-84`
- **Fact**: The implementer records a compile failure before the API existed and six runtime failures with stubbed API bodies, then 8/8 passing after implementation.
- **Implications**: The tests are behaviourally coupled, not compile-only scaffolding; the red logs were not independently reproduced by the orchestrator.
- **Confidence**: Medium
- **Agent**: implementer
- **Related**: HYP-001, VAL-001

### HYP-001: The refinement slice is behaviourally real

- **Claim**: The new tests fail when capture, signal extraction or the policy is stubbed, and the refinement-mode challenger differs from both the champion and the legacy challenger by exactly one 0.06/0.08 parameter delta.
- **Supporting OBS**: OBS-002, OBS-003
- **Why it matters**: If false, the "refinement" arm is cosmetic and the experiment would compare two identical loops.
- **Falsification strategy**: Replace `FailureSignatureExtractor.Extract` with `SessionSignatures.Empty` and run `Run_GivenHarnessRefinement_ExpectedSignatureDrivenChallenger`; it must fail. Inspect the exact assertions in `HarnessRefinementTests.cs:253-304`.
- **Status**: Supported
- **Confidence**: Medium

### VAL-001: Round-1 claim batch

- **Target**: round-1 claim batch (implementer report)
- **Method**: Rebuilt the agents and test projects; ran the focused `HarnessRefinementTests` filter and the unfiltered test assembly through the supplementary runner; ran the Host `train` command into two fresh directories and hashed every JSON file; hashed the frozen/adjacent sources against the pre-round snapshot; grepped the new sources for stubs and forbidden tokens; listed test-file modification times.
- **Evidence examined**: `docs/goal-tholdem-harness/round-1/verification.md`; `.tmp/goal-refinement/round1-focused-verify.log`; `.tmp/goal-refinement/round1-full-verify.log`; `.tmp/goal-refinement/post-legacy-a` and `-b`; `docs/goal-tholdem-harness/round-1/frozen-baseline-hashes.txt`; `docs/goal-tholdem-harness/round-0/baseline-hashes.txt`.
- **Result**: Supported
- **Conclusion**: The slice compiles with 0 errors, focused tests are 8/8, the full assembly is 325/325, both legacy directories reproduce all 7 baseline hashes with no `harness` directory, and the frozen/adjacent sources are byte-identical. Caveats: `dotnet test` is unusable on this machine; the untracked tree prevents a byte diff of existing test files (only the new test file has a 2026-11-08 mtime); zero-rate signals are selectable and are a quality risk.
- **Related**: EVD-004 to EVD-015, OQ-001 to OQ-003

### DEC-002: Accept the round-1 slice and move to the experiment

- **Decision**: Accept R-01/R-02/R-05 as delivered; keep D2 at `Partial` until the real experiment probe produces signatures and divergence; round 2 builds the probe and runs the six dev seeds.
- **Rationale**: VAL-001; D1 is covered at High confidence; the tests assert exact signal counts and parameter deltas, so the slice is not scaffolding.
- **Alternatives considered**: Re-dispatch immediately for a positive-rate floor (rejected: tune on dev seeds with data first); reject the slice for the zero-rate risk (rejected: the spec's selection rule is literal and the risk is measurable).
- **Related**: VAL-001, OQ-001

### OQ-001: Zero-rate signals are selectable

- **Question**: Should a signal with `Rate == 0` and `Sample >= 10` be eligible for the strongest-signal edit, or should the policy require positive evidence of the failure?
- **Needed evidence**: Dev-seed rows showing which signal drove each edit and whether a positive-rate floor changes divergence or zero-effect behaviour.
- **Priority**: P1
- **Raised by**: Round 1 orchestrator review
- **Related**: RISK-002, TODO-001

### OQ-002: Existing-test byte diff is unavailable

- **Question**: Can we prove no existing test file changed, given the Tholdem tree is untracked?
- **Needed evidence**: File mtimes (only the new test file changed) and the suite count 317 to 325; a pre-round byte snapshot would be needed for High confidence.
- **Priority**: P2
- **Raised by**: Round 1 orchestrator review
- **Related**: VAL-001

### OQ-003: `dotnet test` is unusable locally

- **Question**: Is CI the only place the suite runs through VSTest, and does the CI job run the new tests?
- **Needed evidence**: A CI run (unavailable on this machine); the supplementary runner is the local evidence.
- **Priority**: P2
- **Raised by**: Round 1 orchestrator review
- **Related**: VAL-001

### RISK-002: A zero-rate signal can drive an arbitrary edit

- **Risk**: The policy may select a signal that never occurred and change a parameter unrelated to an actual failure; the refinement arm could then diverge without being better.
- **Likelihood**: Medium
- **Impact**: Medium
- **Evidence**: `SignatureDrivenRefinementPolicy.cs:66-70` (highest rate among sample-qualified signals, no positive floor); `FailureSignatureExtractor.cs` emits rate 0 when the count is 0.
- **Mitigation**: Round 2 dev-seed experiment; test a positive-rate or positive-evidence floor before the holdout; record the policy change in a new DEC if adopted.
- **Gate**: —

### TODO-001: Test a positive-evidence selection rule

- **Task**: On dev seeds only, compare the current selection with a rule requiring `Rate > 0`; adopt only if it improves the pre-registered metric, and record the change before the holdout.
- **Priority**: P1
- **Depends on**: OQ-001, the experiment probe
- **Status**: Open
- **Related**: RISK-002

### TODO-002: Build and run the divergence probe

- **Task**: Implement `.tmp/goal-refinement/Probe` per `round-1/experiment-design.md`; run the six dev seeds for both arms; write raw per-iteration rows without aggregation.
- **Priority**: P0
- **Depends on**: the round-1 slice
- **Status**: Done
- **Related**: DEC-002

### OBS-004: DEV probe and step-size control rows exist

- **Source**: `.tmp/goal-refinement/experiment/results.jsonl`, `.tmp/goal-refinement/experiment/results-control.jsonl`
- **Fact**: The pre-registered DEV probe wrote 60 rows (6 seeds x 2 arms x 5 iterations); the `legacy-large` control wrote 30 rows (6 seeds x 5 iterations). Both files parse with the 17-field schema, every row has `totalHands = 120`, and neither run had errors or inert rows. Hashes: `729a3a18…` (main) and `6a7a7bd3…` (control).
- **Implications**: The dev measurement exists and is large enough for the pre-registered comparison; the control isolates step size from signature selection.
- **Confidence**: High
- **Agent**: probe; verified by orchestrator
- **Related**: EVD-016, EVD-019, EVD-022

### HYP-002: Refinement increases divergence and removes zero-effect evaluations vs the shipped baseline

- **Claim**: On the six DEV seeds, the refinement arm's median decision divergence is at least 1.5x the baseline's, its zero-effect rate is lower, and the effect direction is consistent across seeds.
- **Supporting OBS**: OBS-004
- **Why it matters**: This is the pre-registered primary claim; if false, the build has no measured benefit.
- **Falsification strategy**: Recompute the per-seed medians and paired bootstrap from `results.jsonl`; re-derive three sampled divergence rows from the persisted artifacts; check whether any seed reverses direction.
- **Status**: Supported
- **Confidence**: High

### HYP-003: Signature selection adds divergence beyond a larger legacy step

- **Claim**: The refinement arm's divergence is higher than the `legacy-large` control (same 0.08 step, no signatures) by a margin whose paired 95% interval excludes 1.0.
- **Supporting OBS**: OBS-004
- **Why it matters**: If false, most of the gain is step size and the signature layer's value must be stated more narrowly.
- **Falsification strategy**: Compare the per-seed medians of refinement vs `legacy-large`; bootstrap the paired ratio; inspect whether the control matches the refinement arm.
- **Status**: Falsified
- **Confidence**: High

### HYP-004: Signature selection rescues the exact iterations where the baseline is stuck

- **Claim**: On the 7 baseline zero-effect iterations, the refinement arm changes at least one decision on 7/7, while `legacy-large` changes 0/7.
- **Supporting OBS**: OBS-004
- **Why it matters**: This isolates the signature layer's mechanism: it targets the stuck parameter, not just a bigger move.
- **Falsification strategy**: Match rows by seed and iteration; compare `divergenceHands` and `zeroEffect` across the three arms; re-derive the 7 refinement rows if any is disputed.
- **Status**: Supported
- **Confidence**: High

### VAL-002: DEV claim batch

- **Target**: HYP-002, HYP-003, HYP-004
- **Method**: Recomputed per-seed medians, pooled means, paired bootstrap (10,000 resamples, seed 20261108), Fisher exact for the zero-effect counts, and the 7-row paired subset; independently re-derived three sampled divergence rows with a separate verifier project; validated both raw files' SHA-256, line counts, schema and `totalHands`.
- **Evidence examined**: `.tmp/goal-refinement/experiment/results.jsonl` (60 rows, `729a3a18…`), `.tmp/goal-refinement/experiment/results-control.jsonl` (30 rows, `6a7a7bd3…`), `.tmp/goal-refinement/Verify` (three rows re-derived: 5, 15, 6), `docs/goal-tholdem-harness/round-2/dev-summary.md`, `docs/goal-tholdem-harness/round-2/control-analysis.md`.
- **Result**: Supported for HYP-002 and HYP-004; Falsified for HYP-003.
- **Conclusion**: Refinement vs baseline is a large, consistent dev improvement (pooled 5.45x, median per-seed 9x, bootstrap CI [6.75, 11.5]; zero-effect 7/30 to 0/30, Fisher p ~ 0.0105). The signature layer does **not** add a reliable divergence increase over a large-step legacy (pooled 1.26x, median 1.09x, CI [0.80, 1.94]). Its demonstrated unique benefit is the 7/7 rescue of the exact baseline-zero iterations, where the large-step control is 0/7.
- **Related**: EVD-016 to EVD-022, OQ-004, OQ-005

### DEC-003: Accept the DEV result and keep the policy fixed for the holdout

- **Decision**: Accept the dev measurement; word the final claim as "more behavioural exploration and fewer zero-effect proposals than the shipped loop", not "signature direction beats step size on divergence". Run the pre-registered holdout once with the unchanged policy.
- **Rationale**: VAL-002; HYP-002 and HYP-004 are Supported, HYP-003 is Falsified, and changing the policy between dev and holdout would invalidate the pre-registration.
- **Alternatives considered**: Enlarge the signature step to beat the control (rejected: tuning on dev then claiming holdout); drop the signature layer in favour of a large step (rejected: it still leaves 7/30 zero-effect and loses traceability); rerun dev (rejected: no new information).
- **Related**: VAL-002, HYP-002, HYP-003, HYP-004

### OQ-004: Independent dev-row falsifier did not return

- **Question**: Can a second, independent agent reproduce the three sampled divergence rows and the raw-file claims?
- **Needed evidence**: A returned falsifier table; the orchestrator's separate verifier already re-derived 5/15/6, matching the rows.
- **Priority**: P2
- **Raised by**: Round 2 orchestrator
- **Related**: EVD-017, VAL-002

### OQ-005: Holdout split not yet run

- **Question**: Does the pre-registered success rule hold on the six holdout seeds?
- **Needed evidence**: 60 raw holdout rows and the same aggregation, run once with the unchanged policy.
- **Priority**: P0
- **Raised by**: Round 2 orchestrator
- **Related**: DEC-003

### TODO-003: Run the holdout once

- **Task**: Extend the probe with the holdout seed base, run the six holdout seeds for `baseline` and `refinement`, write raw rows, and aggregate against the pre-registered success rule.
- **Priority**: P0
- **Depends on**: OQ-005, the unchanged policy
- **Status**: Done
- **Related**: DEC-003

### OBS-005: HOLDOUT raw rows exist and are the only holdout run

- **Source**: `.tmp/goal-refinement/experiment/results-holdout.jsonl`
- **Fact**: One holdout run wrote 60 rows (6 seeds x 2 arms x 5 iterations), all `totalHands = 120`, zero inert, zero errors, SHA-256 `d47057da15c661fd560245ae96b9714e7882b85ee806c9319f882ab0872e5ca5`. The `legacy-large` control was not run on holdout.
- **Implications**: The pre-registered holdout claim can be evaluated once; dev/control files are unchanged.
- **Confidence**: High
- **Agent**: probe; verified by orchestrator
- **Related**: EVD-023, EVD-026

### HYP-005: The pre-registered holdout success rule is met

- **Claim**: On the six holdout seeds, refinement's median divergence ratio is at least 1.5 with a paired 95% bootstrap lower bound above 1.0, and the refinement zero-effect rate is not higher than the baseline's.
- **Supporting OBS**: OBS-005
- **Why it matters**: This is the one-shot final claim of D9.
- **Falsification strategy**: Recompute the per-seed medians, pooled ratio, bootstrap CI and zero-effect counts from the raw holdout rows; re-derive three sampled rows from the persisted artifacts; check the frozen hashes.
- **Status**: Supported
- **Confidence**: High

### VAL-003: HOLDOUT validation

- **Target**: HYP-005
- **Method**: Ran the holdout aggregation with the pre-registered rule; independently re-derived three holdout divergence rows; checked the raw-file SHA-256, row count, schema, `totalHands` and inert count; rechecked the frozen/adjacent source hashes and the dev/control file hashes.
- **Evidence examined**: `.tmp/goal-refinement/experiment/results-holdout.jsonl` (60 rows, `d47057da…`); `docs/goal-tholdem-harness/round-3/holdout-summary.md`; `.tmp/goal-refinement/Verify` (holdout rows 5, 22, 21 all matched); `round-1/frozen-baseline-hashes.txt`.
- **Result**: Supported
- **Conclusion**: Holdout pooled ratio 5.06x, median per-seed ratio 6.53x (two zero-baseline-median seeds substituted with the pooled mean), bootstrap CI [4.53, 11.5], zero-effect 9/30 baseline vs 0/30 refinement, promotions 0/30 in both. The pre-registered success rule is met. The dev `legacy-large` control still bounds the interpretation: most raw divergence is step size, and the signature layer's distinct benefit is the zero-effect rescue.
- **Related**: EVD-023, EVD-024, EVD-025, EVD-026, DEC-004

### DEC-004: D9 is covered; report the claim with the step-size control caveat

- **Decision**: Mark D9 Covered. The final claim is "refinement more than quintuples decision divergence and removes the zero-effect evaluations observed in the baseline on both dev and holdout", with the explicit caveat that a large-step legacy control captures most of the raw divergence and does not remove the zero-effect cases.
- **Rationale**: VAL-003; the pre-registered rule is met on the untouched holdout, and the control is a dev-only diagnostic rather than a competing pre-registered arm.
- **Alternatives considered**: Claim a signature-specific divergence advantage (rejected: HYP-003 is Falsified, CI includes 1); run the control on holdout (rejected: it would consume holdout data for an unregistered comparison and was not pre-registered).
- **Related**: VAL-003, HYP-003

### TODO-004: Implement R-03 memory and R-04 lineage

- **Task**: Add the canonical harness-memory store and the strategy-lineage reader, integrate memory recording in refinement mode as write-only (no proposal read yet, so the measured arm's challenger choices stay unchanged), and add the tamper/cycle/eviction tests.
- **Priority**: P0
- **Depends on**: the round-1 slice; D9 is closed
- **Status**: Done
- **Related**: DEC-004

### OBS-006: Round-4 memory and lineage are implemented and tested

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessMemoryStore.cs`, `StrategyLineageReader.cs`, `TrainingLoop.cs:98,159-166,267-281`; `WIP/Wip.Tholdem.Tests/Learning/HarnessMemoryTests.cs`, `StrategyLineageReaderTests.cs`
- **Fact**: Memory persists `harness/memory.json` with schema `tholdem.harness-memory.v1`, a SHA-256 over canonical entries, a 64-entry deterministic eviction, dead-end at 3 and tamper refusal; lineage verifies the ledger chain, walks the parent chain root-first with a 64-depth cap and stable missing-parent/cycle/artifact-hash/ledger refusals. Memory is write-only in refinement mode; the policy context has no memory member.
- **Implications**: R-03 and R-04 are delivered; the measured arm's challenger choices must be unchanged.
- **Confidence**: High
- **Agent**: implementer; verified by orchestrator
- **Related**: EVD-027, EVD-028, EVD-029

### HYP-006: R-03/R-04 meet the requirements without changing the measured policy

- **Claim**: The memory and lineage implementations satisfy their acceptance criteria, and a post-round-4 refinement run chooses the same challengers as the pre-round-4 probe for the same seed/options.
- **Supporting OBS**: OBS-006
- **Why it matters**: If the policy changed, the accepted holdout result no longer describes the current arm.
- **Falsification strategy**: Run the 10 memory and 6 lineage tests; inspect the pre-round-4 probe ledger at `.tmp/goal-refinement/Probe4/.../standard/ledger.json` (written 13:09:50, before the integration) for the pinned IDs; run the full suite; re-run the legacy CLI equivalence.
- **Status**: Supported
- **Confidence**: High

### VAL-004: Round-4 validation

- **Target**: HYP-006
- **Method**: Rebuilt the Agents and test projects; ran the two focused test classes and the unfiltered assembly through the supplementary runner; ran the Host CLI into two fresh legacy directories and compared all hashes to the round-0 baseline; checked the pre-round-4 Probe4 ledger for the pinned challenger IDs; rechecked the frozen/adjacent hashes; scanned the new sources for stubs and forbidden tokens; listed test-file modification times.
- **Evidence examined**: `.tmp/goal-refinement/round4-full-verify.log` (341/341); focused logs (10/10, 6/6); `round4-legacy-verify-a/b` (7/7, no memory); `.tmp/goal-refinement/Probe4/bin/Release/net10.0/probe-data/standard/ledger.json` (IDs `d24dcf6d`, `c34333fe`, `86f42856` at 13:09:50); `round-1/frozen-baseline-hashes.txt`.
- **Result**: Supported
- **Conclusion**: R-03/R-04 are delivered; 341/341 tests pass; legacy is byte-identical with no memory file; the policy is unchanged for the pinned seed; only the authorized one-line scratch predicate changed in an existing test.
- **Related**: EVD-027 to EVD-034, DEC-005

### DEC-005: Authorize the one-line stale-assertion scope correction

- **Decision**: Change `HarnessRefinementTests.cs:72-74` from `*.json` to `hand-*.json` and add a comment; no other assertion changes.
- **Rationale**: `harness/memory.json` is a new persisted product artifact, not trajectory scratch. The original predicate's intent was "no captured hand file survives"; the narrowed predicate preserves that intent, and the memory file's existence/hash/tamper/eviction/legacy absence are covered by the new memory tests. The pre-fix failure was observed (`TOTAL 340 PASSED 339 FAILED 1`).
- **Alternatives considered**: Move memory outside `harness` (rejected: less coherent artifact layout); leave the suite red (rejected: the test is stale, not the product); add an exception for `memory.json` (rejected: the narrowed predicate is clearer).
- **Related**: VAL-004, EVD-032

### DEC-006: Accept round 4; D3 is Covered

- **Decision**: Accept R-03/R-04; mark D3 Covered; proceed to R-06 bootstrap and R-07/R-08 guard/safety controls.
- **Rationale**: VAL-004; memory/lineage tests and the full suite pass, legacy is unchanged, and the measured policy is invariant.
- **Alternatives considered**: Feed memory into the policy now (rejected: it would change the measured arm and require a new pre-registration); stop at the experiment (rejected: the requirements still have D4–D8 open).
- **Related**: VAL-004

### RISK-003: Memory becomes a proposal input in a later round and invalidates the holdout arm

- **Risk**: R-05's full refinement policy will read memory/lineage, changing challenger choices; the accepted holdout result describes the round-1 write-only arm, not that future arm.
- **Likelihood**: High (by design)
- **Impact**: Medium
- **Evidence**: `RefinementContext` has no memory member today; the write-only integration is deliberate.
- **Mitigation**: Introduce a new opt-in mode/version for memory-aware refinement; run a new dev then holdout comparison for that arm before claiming an improvement; never silently change the existing `HarnessRefinement` behavior.
- **Gate**: —

### TODO-005: Implement R-06 bootstrap modes

- **Task**: Add `None`/`Frozen`/`Updating` bootstrap modes that load champion, memory and lineage from a prior data directory or snapshot, record the mode and memory hashes, and prove deterministic artifacts.
- **Priority**: P0
- **Depends on**: R-03/R-04
- **Status**: Done
- **Related**: RISK-003

### OBS-007: Round-5 bootstrap and safety controls exist

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessBootstrap.cs`, `HarnessBootstrapMode.cs`, `HarnessBootstrapSummary.cs`; `TrainingLoop.cs:91-106,118-121,293,309-328`; `WIP/Wip.Tholdem.Tests/Learning/HarnessBootstrapTests.cs`, `HarnessSafetyTests.cs`
- **Fact**: `None`/`Frozen`/`Updating` bootstrap modes load a verified source champion, memory and lineage into a fresh target; Frozen leaves memory bytes unchanged, Updating appends one record per completed iteration, and the run summary records mode/source/champion/lineage count/hashes/writability. Safety tests cover an injection-looking key, a poisoned `promote` entry and a 32-source scan with no process/network/compiler token.
- **Implications**: R-06 and R-08 are delivered; the measured arm remains unchanged (`Bootstrap` null and the pinned challengers).
- **Confidence**: High
- **Agent**: implementer; verified by orchestrator
- **Related**: EVD-035, EVD-036, EVD-039

### HYP-007: R-06/R-08 meet the requirements without changing the measured arm

- **Claim**: Bootstrap modes persist the expected frozen/updating memory behavior with stable refusals, and the pinned refinement arm still chooses `d24dcf6d`, `c34333fe`, `86f42856` with `Bootstrap == null`.
- **Supporting OBS**: OBS-007
- **Why it matters**: If the arm changed, the accepted holdout result no longer describes the current code.
- **Falsification strategy**: Run the 10 bootstrap and 3 safety tests; inspect `TrainingRun.Bootstrap` and the pinned challengers; run the full suite and the legacy CLI hash comparison.
- **Status**: Supported
- **Confidence**: High

### VAL-005: Round-5 validation

- **Target**: HYP-007
- **Method**: Rebuilt Agents and tests; ran the focused bootstrap and safety classes and the unfiltered assembly; ran the Host CLI twice into fresh legacy directories and compared all hashes; rechecked the frozen/adjacent hashes; scanned the Learning sources for stubs and forbidden tokens; listed test-file modification times.
- **Evidence examined**: `.tmp/goal-refinement/round5-focused-bootstrap-verify.log` (10/10); `round5-focused-safety-verify.log` (3/3); `round5-full-verify.log` (354/354); `round5-legacy-verify-a/b` (7/7, no harness); `round-1/frozen-baseline-hashes.txt`; `HarnessBootstrapTests.cs:28-102` (frozen/updating byte assertions).
- **Result**: Supported
- **Conclusion**: R-06/R-08 are delivered; 354/354 tests pass; legacy is byte-identical; no existing test changed in this round; only pre-existing warnings remain.
- **Related**: EVD-035 to EVD-039

### DEC-007: Accept round 5; D4 is Covered, D5 remains Partial

- **Decision**: Accept R-06/R-08; mark D4 Covered; keep D5 Partial until the R-07 multi-edit guard exists. Proceed to R-09/R-10 deterministic skills.
- **Rationale**: VAL-005; bootstrap and safety controls are proven, but the guard expansion in R-07 depends on a multi-edit proposal surface that the skills round introduces.
- **Alternatives considered**: Implement a synthetic edit model now (rejected: no product surface uses it yet); mark D5 Covered from the source scan alone (rejected: that would overclaim the guard).
- **Related**: VAL-005, OQ-006

### OQ-006: R-07 guard expansion is still open

- **Question**: Does the multi-edit guard need to reject frozen-target, out-of-domain, oversized and memory-only edits before the skills round introduces those edit kinds?
- **Needed evidence**: The R-09 `HarnessEdit` surface; a guard test for each edit kind.
- **Priority**: P1
- **Raised by**: Round 5 orchestrator
- **Related**: DEC-007

### RISK-004: Bootstrap copies verified bytes, then the source could change

- **Risk**: `HarnessBootstrap.Load` verifies source memory through the store and then reads the bytes for a verbatim copy; a concurrent source mutation between those reads could be copied.
- **Likelihood**: Low
- **Impact**: Low
- **Evidence**: `HarnessBootstrap.cs` load/copy path; the target's own later `Load` would refuse a mismatched hash.
- **Mitigation**: Sources are static in tests and normal use; a future single-read snapshot would remove the window.
- **Gate**: —

### TODO-006: Implement R-09/R-10 deterministic skills

- **Task**: Decompose `StrategyAgent` into four deterministic skills with bounded data parameters, preserve byte-identical behavior when skills are absent/default, and allow skill edits through the refinement policy under the unchanged gate.
- **Priority**: P0
- **Depends on**: R-06; the current 354-test baseline
- **Status**: Done
- **Related**: DEC-007, OQ-006

### OBS-008: Round-6 skills are implemented behind an opt-in

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/Skills/StrategySkillSet.cs`, `StrategySkillSetJson.cs`; `StrategyGenome.cs`, `StrategyArtifact.cs`, `StrategyAgent.cs`; `Refinement/SignatureDrivenRefinementPolicy.cs`, `RefinementGuard.cs`; `TrainingOptions.cs`, `TrainingLoop.cs`; `WIP/Wip.Tholdem.Tests/Learning/HarnessSkillsTests.cs`
- **Fact**: `StrategyGenome` carries an optional `StrategySkillSet`; absent skills keep the exact legacy decision body and artifact bytes. With skills present, a fixed preflop/postflop/sizing/bluff pipeline runs, every action is validated, and the artifact emits a `skills` object only when non-null. Skill refinement is a separate `SkillRefinement` opt-in and a `RefinementGuard` rejects out-of-domain, multi-parameter and inert skill edits.
- **Implications**: R-09/R-10 are delivered; the measured arm must remain unchanged with `SkillRefinement=false`.
- **Confidence**: High
- **Agent**: implementer; verified by orchestrator
- **Related**: EVD-040 to EVD-047

### HYP-008: The skill change preserves the measured arm and the legacy artifact

- **Claim**: With `SkillRefinement=false`, the pinned challengers are unchanged; with skills absent, the v1 artifact bytes/hash are unchanged; the five gate components are byte-identical; and a 240-hand default-skills replay is byte-identical to the legacy agent.
- **Supporting OBS**: OBS-008
- **Why it matters**: If false, the accepted holdout result no longer describes the current arm and the artifact schema broke history.
- **Falsification strategy**: Run the 15 skill tests; recompute the v1 artifact hash from a real legacy CLI run; compare the five gate hashes to the round-1 snapshot; run the full suite; inspect the pinned-challenger test.
- **Status**: Supported
- **Confidence**: High

### VAL-006: Round-6 validation

- **Target**: HYP-008
- **Method**: Rebuilt Agents and tests; ran the focused skill class and the unfiltered assembly; ran the Host CLI twice into fresh legacy directories and hashed every file, including the v1 artifact; compared the five gate component hashes to the round-1 snapshot; read the pinned-challenger and 240-hand equivalence tests; scanned the new sources for stubs and forbidden tokens; listed test-source modification times.
- **Evidence examined**: `.tmp/goal-refinement/round6-focused-skills-verify.log` (15/15); `round6-full-verify.log` (369/369); `round6-legacy-verify-a/b` (7/7, v1 `6675d797…`); `round-6/frozen-after-skills.txt`; `HarnessSkillsTests.cs:44-315`.
- **Result**: Supported
- **Conclusion**: R-09/R-10 are delivered with the strict invariant intact: 369/369 tests, legacy 7/7, v1 artifact hash unchanged, five gate components unchanged, pinned challengers unchanged. `StrategyGenome`, `StrategyArtifact` and `StrategyAgent` intentionally changed as schema-adjacent files; their new hashes are recorded in `round-6/frozen-after-skills.txt`.
- **Related**: EVD-040 to EVD-047, DEC-008

### DEC-008: Accept round 6; D5/D6 are Covered

- **Decision**: Accept R-09/R-10; mark D6 Covered and D5 Covered (the skill edit surface now has the R-07 guard). Record the schema-adjacent change to `StrategyGenome`/`StrategyArtifact` and replace the working frozen snapshot with `round-6/frozen-after-skills.txt` while keeping the round-1 file as history.
- **Rationale**: VAL-006; the five gate components are unchanged, the v1 artifact hash is unchanged, and the guard rejects the three edit classes.
- **Alternatives considered**: Keep the round-1 snapshot for the schema-adjacent files (rejected: future checks would flag an intentional, hash-preserving schema extension as drift); leave D5 Partial (rejected: the guard now covers the edit surface that exists).
- **Related**: VAL-006, RISK-005

### RISK-005: Future genome fields could break recorded artifact hashes

- **Risk**: The optional `skills` member proved hash-preserving only while it is omitted for null. A future always-emitted field or a default `Skills` set would change every artifact hash and invalidate pinned evidence.
- **Likelihood**: Medium
- **Impact**: High
- **Evidence**: `StrategyArtifact.cs` conditional emission; `StrategyGenome.Skills = null` default.
- **Mitigation**: Keep optional members omitted when null; add a legacy-bytes test for any new genome field; never default a schema field to non-null.
- **Gate**: —

### TODO-007: Implement R-11 ablation and R-12 refiner evolution

- **Task**: Add the bounded six-configuration ablation runner with a canonical report and no pointer writes, and extend `ProposerGenome` with a bounded refinement strategy that the existing meta-loop can evolve under the unchanged gate/fresh holdout.
- **Priority**: P0
- **Depends on**: R-09/R-10
- **Status**: R-11 done; R-12 in progress
- **Related**: DEC-008

### OBS-009: Round-7 ablation runner is additive and emits a canonical report

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessAblationOptions.cs`, `HarnessAblationConfiguration.cs`, `HarnessAblationReport.cs`, `HarnessAblationRunner.cs`; `WIP/Wip.Tholdem.Tests/Learning/HarnessAblationTests.cs`
- **Fact**: The runner executes six fixed configs (`none`, `signatures`, `memory`, `lineage`, `skills`, `all`) in fresh scratch directories, emits a canonical 2259-byte `tholdem.ablation.v1` report (no BOM/CR/LF), and never opens the caller-root champion or proposer pointers. The gate-shaped run produced no promotions in any arm; `memory`/`lineage`/`skills`/`all` matched `signatures` for this seed, while `none` differed.
- **Implications**: R-11 is delivered; D7 is Covered.
- **Confidence**: High
- **Agent**: implementer; verified by orchestrator
- **Related**: EVD-048 to EVD-053

### HYP-009: R-11 satisfies its acceptance without touching product behavior

- **Claim**: Six configs produce one canonical report, repeat runs are byte-identical, pointer bytes are untouched, out-of-bound options refuse before writing, and the ablation run does not alter the legacy or measured loop.
- **Supporting OBS**: OBS-009
- **Why it matters**: A measurement tool that writes pointers or changes behavior is not a safe ablation.
- **Falsification strategy**: Run the 6 focused tests; inspect the 2259-byte report and its six rows; hash the caller-root pointers; run the full suite and the legacy CLI comparison.
- **Status**: Supported
- **Confidence**: High

### VAL-007: Round-7 validation

- **Target**: HYP-009
- **Method**: Rebuilt Agents and tests; ran the focused ablation class and the unfiltered assembly; inspected the gate-shaped report bytes/hash/schema/row order; checked the caller-root pointer hashes and root entries; ran the Host CLI twice into fresh legacy directories and compared all hashes; rechecked the round-6 frozen snapshot.
- **Evidence examined**: `.tmp/goal-refinement/round7-focused-ablation-verify.log` (6/6); `round7-full-verify.log` (375/375); `.tmp/goal-refinement/round7-ablation-root-gate/ablation/report.json` (2259 bytes, `29aecdc8…`, six configs, no CR/LF); caller-root pointers `9feccc6a…` and `210784c8…`; `round7-legacy-verify-a/b` (7/7); `round-6/frozen-after-skills.txt`.
- **Result**: Supported
- **Conclusion**: R-11 is delivered additively: 375/375 tests, canonical report, pointers unchanged, legacy 7/7, no existing product or test file edited. The untested null-mean branch is recorded as OQ-007.
- **Related**: EVD-048 to EVD-053

### DEC-009: Accept round 7; D7 is Covered

- **Decision**: Accept R-11; mark D7 Covered; proceed to the final implementation, R-12.
- **Rationale**: VAL-007; the runner is additive, the report is canonical and reproducible, and the pointer sentinels are byte-stable.
- **Alternatives considered**: Add a test-only proposer seam for the null-mean branch (rejected: it would require a product seam not needed by the runner); combine R-12 into this round (rejected: separate risk and verification).
- **Related**: VAL-007, OQ-007

### OQ-007: The null-mean ablation row branch is untested

- **Question**: Does the report writer correctly emit `meanBbPer100:null` when a configuration completes zero evaluations?
- **Needed evidence**: A configuration/seed that produces an inert proposal in every iteration, or a test-only proposer seam.
- **Priority**: P2
- **Raised by**: Round 7 orchestrator
- **Related**: EVD-049

### TODO-008: Implement R-12 refiner evolution

- **Task**: Add the bounded `RefinementStrategy` to `ProposerGenome`, the policy selection, and the `MetaLoop` opt-in wiring while preserving default meta artifact bytes and the frozen gate/fresh holdout.
- **Priority**: P0
- **Depends on**: R-11
- **Status**: Done
- **Related**: DEC-009

### OBS-010: Round-8 refiner evolution is delivered with default hashes preserved

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/Refinement/RefinementStrategy.cs`; `ProposerGenome.cs`, `MetaArtifact.cs`, `MetaEvaluation.cs`, `MetaEvaluationJson.cs`, `MetaLoop.cs`, `SignatureDrivenRefinementPolicy.cs`, `TrainingOptions.cs`, `TrainingLoop.cs`; `WIP/Wip.Tholdem.Tests/Learning/HarnessRefinerTests.cs`
- **Fact**: `ProposerGenome` carries a two-value `RefinementStrategy` (default `SessionDriven`); `MetaArtifact`/`MetaEvaluation` emit it only when non-default; `MetaRunOptions.UseHarnessRefinement` wires the episode runner to `TrainingOptions.RefinementStrategy`; `SessionDriven` delegates to the legacy proposer; the default meta artifact remains `tholdem-proposer-v1-24c2109a` (344 bytes, hash `24c2109a…`) and the default meta document remains `2f2888d5…` (1545 bytes).
- **Implications**: R-12 is delivered; D8 is Covered.
- **Confidence**: High
- **Agent**: implementer; verified by orchestrator
- **Related**: EVD-054 to EVD-058

### HYP-010: R-12 preserves the default and frozen surfaces

- **Claim**: The default meta artifact/document bytes are unchanged, the five frozen components and pinned manifest are unchanged, the focused refiner tests pass, and the full suite is 386/386.
- **Supporting OBS**: OBS-010
- **Why it matters**: A meta-schema change that moves default hashes would invalidate the RSI evidence and the frozen gate.
- **Falsification strategy**: Run the 11 refiner tests and the full suite; re-run the meta probe before/after; compare the 13-file frozen snapshot; run the legacy CLI.
- **Status**: Supported
- **Confidence**: High

### VAL-008: Round-8 validation

- **Target**: HYP-010
- **Method**: Rebuilt Agents/tests; ran the focused `HarnessRefiner` class and the unfiltered assembly; re-ran the meta probe and compared pre/post artifact and document bytes; rechecked the 13-file frozen snapshot; ran the Host CLI twice and compared the 7 baseline hashes.
- **Evidence examined**: `.tmp/goal-refinement/round8-focused-refiner-verify.log` (11/11); `round8-full-verify.log` (386/386); `round8-meta-baseline.txt`/`round8-meta-after.txt` (identical artifact/document bytes); `round-6/frozen-after-skills.txt` (13/13); `round8-legacy-verify-a/b` (7/7).
- **Result**: Supported
- **Conclusion**: R-12 is delivered with the default bytes and frozen surface intact; 386/386 tests pass; legacy 7/7.
- **Related**: EVD-054 to EVD-058

### DEC-010: Accept R-12; D8 is Covered; open the meta-document path remediation

- **Decision**: Accept R-12; mark D8 Covered. Do not mark the goal complete yet: a pre-existing `MetaLoop` path defect is a P1 item that must be fixed before the final claim.
- **Rationale**: VAL-008; R-12's own contract is met, but the meta evaluation document is written to a nested path while the promote reader uses the non-nested path.
- **Alternatives considered**: Ignore the defect as out of scope (rejected: the meta promote path is part of the system this goal claims to improve); fix it without a regression test (rejected: the harness requires a failing-then-passing test).
- **Related**: VAL-008, OQ-008, TODO-009

### OQ-008: Meta evaluation documents are written to a nested path

- **Question**: Does `MetaLoop` write `meta/evaluations/<file>.json`, or `meta/evaluations/meta/evaluations/<file>.json`, and can `POST /api/rsi/{jobId}/promote` find it?
- **Needed evidence**: A real `MetaLoop.Run` followed by `MetaStore.TryReadEvaluationDocument`; a directory listing of the meta tree; the web promote path read.
- **Priority**: P1
- **Raised by**: Round 8 implementer flag, verified statically by orchestrator
- **Related**: RISK-006, TODO-009

### RISK-006: Meta promote cannot find the evaluation document

- **Risk**: `MetaLoop.Run` passes `meta/evaluations` to `MetaStore.WriteEvaluationDocument`, which prepends `meta/evaluations` again; `TryReadEvaluationDocument` reads the non-nested path, so the RSI promote endpoint may return a missing-document refusal for every completed meta iteration.
- **Likelihood**: High (the path arithmetic is deterministic)
- **Impact**: High (meta promotion is unusable)
- **Evidence**: `MetaLoop.cs:530`, `MetaStore.cs:788-806` (line numbers in the current tree)
- **Mitigation**: Pass the data directory to `WriteEvaluationDocument` and add a real-loop regression test that reads the document back through `TryReadEvaluationDocument`; rerun the full suite and the meta promote tests.
- **Gate**: —

### TODO-009: Fix the meta-document path and prove it

- **Task**: Correct the `MetaLoop` write target, add a real `MetaLoop.Run` → `TryReadEvaluationDocument` regression test, and prove the nested path is never created.
- **Priority**: P1
- **Depends on**: OQ-008, R-12
- **Status**: Done
- **Related**: RISK-006

### OBS-011: The meta-evaluation path is fixed and regression-tested

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/MetaLoop.cs:530`; `WIP/Wip.Tholdem.Tests/Learning/MetaDocumentPathTests.cs:16-59`
- **Fact**: `MetaLoop.Run` now passes the data directory to `MetaStore.WriteEvaluationDocument`, so the document lands at `<data>/meta/evaluations/<candidate>-vs-<incumbent>.json`, which `TryReadEvaluationDocument` and the web promote audit read. A real `MetaLoop.Run` test reads the document back, asserts byte equality, and asserts the nested `meta/evaluations/meta/evaluations` path does not exist.
- **Implications**: The P1 meta promote audit path is repaired; the document schema/hash/decision are unchanged.
- **Confidence**: High
- **Agent**: remediation implementer; verified by orchestrator
- **Related**: EVD-060, EVD-061, EVD-062

### HYP-011: The path fix closes OQ-008 without changing the document

- **Claim**: After the one-line fix, a completed meta iteration's document is readable at the non-nested path, the nested path is absent, the document bytes match the in-memory canonical text, and the full suite is 387/387.
- **Supporting OBS**: OBS-011
- **Why it matters**: If the document moved but changed bytes, the audit hash or schema would break.
- **Falsification strategy**: Run the focused `MetaDocumentPath` test and the full suite; inspect the path assertions; re-run the legacy CLI and frozen snapshot.
- **Status**: Supported
- **Confidence**: High

### VAL-009: Round-9 validation

- **Target**: HYP-011
- **Method**: Rebuilt Agents/tests; ran the focused `MetaDocumentPath` test and the unfiltered assembly; read the path arithmetic and the regression test; ran the Host CLI twice and compared the 7 baseline hashes; rechecked the 13-file frozen snapshot.
- **Evidence examined**: `.tmp/goal-refinement/round9-focused-metapath-verify.log` (1/1); `round9-full-verify.log` (387/387); `MetaDocumentPathTests.cs:38-53`; `round9-legacy-verify-a/b` (7/7); `round-6/frozen-after-skills.txt`.
- **Result**: Supported
- **Conclusion**: OQ-008/RISK-006 is closed; 387/387 tests pass; legacy 7/7; the document schema, hash and decision are untouched.
- **Related**: EVD-060, EVD-061, EVD-062, DEC-011

### DEC-011: Close the P1 meta path item; the definition of done is met

- **Decision**: Accept the meta path fix; mark OQ-008/RISK-006 resolved; the goal is complete at 9/9 dimensions.
- **Rationale**: VAL-009; the read-back path is proven by a real meta-loop regression test, the full suite is green, and the frozen/legacy invariants hold.
- **Alternatives considered**: Leave the pre-existing bug for a follow-up (rejected: it makes the meta promote audit unusable and the goal claims the meta surface); change the store instead of the caller (rejected: the store's data-directory contract is shared with the reader).
- **Related**: VAL-009, OQ-008

### DEC-012: Accept the zero-rate selection residual

- **Decision**: Do not add a positive-rate floor to `SignatureDrivenRefinementPolicy` for this goal; keep OQ-001 as an accepted P1 residual.
- **Rationale**: The shipped policy passed the pre-registered holdout with the zero-rate behavior in place; changing it after the holdout would invalidate the measured arm, and no adverse effect was observed. A future goal can ablate a positive-rate floor on dev seeds with a new holdout.
- **Alternatives considered**: Change the policy now and re-run a new holdout (rejected: time and p-hacking risk); silently ignore OQ-001 (rejected: it stays recorded).
- **Related**: OQ-001, RISK-002, EVD-025
