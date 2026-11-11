# Goal Journal — goal-tholdem-enhancement

> Append-only. See the stretch-goal-harness evidence protocol for entry format.
> Append new entries below the marker; only HYP/TODO `Status` fields may be edited in place.

---

### OBS-001: Reviewed commit is checked out

- **Source**: `git rev-parse HEAD` → `11eca9e83602b8ae485c815788d1e8b3df271a5e`; branch `WIP`
- **Fact**: The working tree is exactly the commit the specification reviewed; `git status --porcelain` was empty before this goal's files were added.
- **Implications**: Baseline hashes recorded now are attributable to the reviewed revision.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-001

### OBS-002: VSTest cannot run locally

- **Source**: `dotnet test WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` → exit 1; testhost `Win32Exception (5): Access is denied` in `SetParentProcessExitCallback`
- **Fact**: The documented local limitation reproduces on this machine; the failure is at testhost startup, not in any test.
- **Implications**: Local test evidence must come from the compiled-assembly runner; CI remains authoritative.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-002, C8

### OBS-003: The engine and simulator already support 2–6 seats

- **Source**: `WIP/Wip.Tholdem/Engine/HoldemHandEngine.cs:40-193`, `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:48-247`
- **Fact**: `HoldemHandEngine.Start` accepts 2–6 seats with side pots and layered showdown; `MatchSimulator.Run(IReadOnlyList<IPokerAgent>, ...)` runs N seats, seeds hand `n` with `Derive(seed, n, 0, 0, 0)` and rotates the button `(n-1) % seats`.
- **Implications**: The four-seat evaluator can be built additively on the simulator; the missing piece is paired focal-seat evaluation, not engine support.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-003

### OBS-004: The v1 evaluator is heads-up and per-seed

- **Source**: `WIP/Wip.Tholdem.Agents/Learning/MatchEvaluator.cs:20-110`; `PromotionPolicy.cs:1-58`; `PromotionVerifier.cs:20-59`
- **Fact**: `MatchEvaluator.Evaluate` runs two-seat matches in both seat assignments per seed, averages per-seed bb/100 and uses a normal CI over seeds; `StatisticalPromotionPolicy` promotes when `LowerBound > Margin` and `Seeds >= MinSeeds`; `PromotionVerifier` re-derives that rule.
- **Implications**: v2 must be a new, separately versioned contract; v1 stays byte-identical (C5).
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-004

### OBS-005: `MatchSimulator` does not expose per-hand outcomes

- **Source**: `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:193-246`; `SimulationReport.cs:1-9`
- **Fact**: The simulator accumulates `HandOutcome` locally and returns only aggregate `AgentMetrics`; there is no public path to per-hand focal-seat stacks.
- **Implications**: EVA-005 needs an additive `RunDetailed` that returns both the unchanged report and the per-hand outcomes.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-005

### DEC-001: Goal directory and phase scope

- **Decision**: Persist this campaign under `docs/goal-tholdem-enhancement/`; activate Milestones A and B first; keep D1–D12 as the full goal.
- **Owner**: agent
- **Status**: approved
- **Rationale**: The specification makes Milestone B the "first real P0 gate"; engine support for 4 seats already exists (OBS-003), so the paired evaluator is the shortest credible increment.
- **Alternatives considered**: Starting with equity/policy (Milestone C) was rejected because a stronger policy cannot be honestly promoted until the evaluation gate exists.
- **Related**: OBS-003, OBS-004, goal.md

### DEC-002: Phase-scoped coverage gate

- **Decision**: G3 is evaluated against the active milestone's dimensions; future dimensions are recorded as `None` but do not fail G3 during Milestones A/B.
- **Owner**: agent
- **Status**: approved
- **Rationale**: A literal all-dimensions rule would fail before any round could start. The scoping is recorded so no later round can claim full coverage from it.
- **Alternatives considered**: Narrowing the goal to R1 only, rejected because it would drop user-named mechanisms (EQ-002, LLM-004).
- **Related**: goal.md

### RISK-001: VSTest unavailability can hide suite regressions

- **Risk**: The supplementary runner may not execute every xUnit feature (fixtures, skips, collection behavior), so its pass count may differ from CI.
- **Likelihood**: Medium
- **Impact**: Medium
- **Evidence**: OBS-002; `.tmp/tholdem-verify/RunXunit/Program.cs`
- **Mitigation**: Run the full Tholdem suite through the runner before and after each round; compare counts; never claim a CI result.
- **Gate**: G4

### CAP-001: Model credentials are present but not exercised

- **Capability**: External LLM proposal path
- **Check**: `.secrets/deepseek.env` exists (names inspected only); no model call made; default evaluation path must stay offline (ART-012).
- **Result**: available-but-unverified. LLM-arm claims are deferred to Milestone E; no LLM evidence is claimed before an equal-budget ablation exists.
- **Related**: ART-012, LLM-004

### OQ-001: Which four-seat CLI surface should own v2 promotion?

- **Question**: Should v2 be a new `evaluate --seats 4` path, a new `promote4` command, or a library-only API first?
- **Needed evidence**: `Wip.Tholdem.Host/Commands/EvaluateCommand.cs` and `PromoteCommand.cs` call-map; decision on whether v1 command semantics can stay unchanged.
- **Priority**: P1 blocking for SIM-001's CLI acceptance row, not blocking the evaluator library.
- **Raised by**: Round 0 (orchestrator)
- **Related**: SIM-001, ART-004

### OBS-006: Baseline suite is green at 446 tests

- **Source**: `.tmp/tholdem-verify/goal-enhancement-baseline-full.log` final line: `TOTAL 446 PASSED 446 FAILED 0 SKIPPED 0`; DLL SHA-256 `4d0d8acb3ecf66d95823182411d40e89b298eea5cc5fb62e2f8633bba8abedf2`
- **Fact**: The full Tholdem test assembly passes through the supplementary runner before any change, with no skipped tests.
- **Implications**: Any post-round failure is attributable to the round; any pass-count change must be explained by added tests.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-007, EVD-008, EVD-002

### OBS-007: The default heuristic strategy is worse than always-folding in four-way play

- **Source**: scratch probe `.tmp/tholdem-verify/GoalProbe/Program.cs` run 2026-11-10; 24 paired blocks of 20 hands × 4 rotations, focal `StrategyAgent(StrategyGenome())` vs focal `FoldAlwaysAgent`, opponents fixed [TightAggressive, PotOdds(120), RandomLegal]
- **Fact**: The default `StrategyAgent` focal averaged **-263 bb/100** relative to a focal agent that folds to every bet (mean -263.2, sd 828.1, 24 blocks); a separate 100-hand mixed-field run had the default strategy at -140.7 bb/100 while both `TightAggressiveAgent` seats were positive.
- **Implications**: Milestone C cannot claim policy improvement from this genome; the four-way evaluator must treat the current champion as weak. The control pair for the evaluator sanity gate should not assume the genome is "stronger".
- **Confidence**: Medium (single seed family, scratch probe, not a persisted test)
- **Agent**: orchestrator
- **Related**: EVD-007, HYP-001, POL-001

### HYP-001: A tight-passive scripted policy beats an always-fold incumbent in four-way cash

- **Claim**: A `StrategyAgent` at (raise 0.90, call 0.85, bluff 0, sizing 1.0) has a positive paired bb/100 difference against a focal always-fold policy over balanced four-seat blocks.
- **Supporting OBS**: OBS-007
- **Why it matters**: It is the "known stronger scripted agent vs known dominated agent" control for Milestone B; if false, the evaluator sanity gate needs a different control pair.
- **Falsification strategy**: Run 96 disjoint pilot blocks (seed family 500..595, 20 hands, 4 rotations, fixed opponents) and compute the paired block mean; a mean ≤ 0 or a t-statistic < 2 falsifies the direction.
- **Status**: Supported
- **Confidence**: Medium

### VAL-001: Pilot supports HYP-001 at 96 blocks

- **Target**: HYP-001
- **Method**: Ran the scratch probe with 96 blocks (seed family 500..595), focal nit vs focal fold, 20 hands × 4 rotations per block; aggregated per-block bb/100
- **Evidence examined**: `.tmp/tholdem-verify/goal-pilot-fold-vs-callany.log` final line: `blocks=96 hands_per_block=80 mean=45.92 sd=159.56 se=16.28 t=2.82 ms=19129` (note: file name says call-any but the run was nit-vs-fold; the log line is authoritative)
- **Result**: Supported
- **Conclusion**: Mean +45.9 bb/100, 95% normal lower bound ≈ +14.0. The direction holds at 96 pilot blocks; the final holdout must use disjoint seeds and a pre-registered margin.
- **Related**: HYP-001, OBS-007

### VAL-002: Intent audit found two DoD omissions; both repaired

- **Target**: round-0 claim batch (goal contract)
- **Method**: Independent Intent Auditor compared 15 named mechanisms against `goal.md` and `source-requirements.md`; I opened each cited DoD row
- **Evidence examined**: `docs/goal-tholdem-enhancement/intent-audit.md` — rows 5 and 14 returned REMOVED: EVA-008 had no explicit DoD check (A5 claimed one), and TST-001/SIM-008 legacy preservation existed only as prose constraint C1
- **Result**: Falsified (for the audit claims as first written); repaired, not waived
- **Conclusion**: The omissions were editorial, not scope decisions. I restored both: D1 now carries TST-001 + SIM-008 with a 446/446 and golden-replay check; D6 now carries EVA-007..011 with plan-hash, disjoint-domain and anchor-regression checks; D11 gained an ART-005/006 deny-test check. No mechanism was dropped and no user approval is needed because nothing was excluded.
- **Related**: intent-audit.md, DEC-003, EVA-008, TST-001

### DEC-003: Resolve intent audit by restoring the two omitted checks

- **Decision**: Restore EVA-008 and TST-001/SIM-008 as explicit DoD checks rather than ask the user to approve dropping them.
- **Owner**: agent
- **Status**: approved
- **Rationale**: The checks are named in the user's request; the omission was a drafting error in my DoD, not a proposed exclusion. Restoring is the intent-faithful resolution and requires no user-owned scope decision.
- **Alternatives considered**: Asking the user to choose between restore/drop was rejected because dropping would remove a user-named mechanism without an approved exclusion.
- **Related**: VAL-002, goal.md, OQ-002

### OQ-002: Does the repaired DoD satisfy the intent auditor?

- **Question**: After restoring EVA-008 and TST-001/SIM-008 checks, does the independent auditor return KEPT for all 15 mechanisms?
- **Needed evidence**: A re-audit table over the same 15 rows against the updated goal.md.
- **Priority**: P0 blocking Round 1.
- **Raised by**: Round 0 (orchestrator)
- **Related**: VAL-002, DEC-003

### VAL-003: Repaired DoD passes the independent re-audit

- **Target**: OQ-002
- **Method**: Re-read `intent-audit.md` after the repair; checked rows 5, 14 and 15 cite the exact restored checks in D6, D1 and D11; checked the file is UTF-8 without BOM
- **Evidence examined**: `docs/goal-tholdem-enhancement/intent-audit.md` final counts `KEPT 15 / REPLACED 0 / REMOVED 0`, `QUESTION: NONE`; 3,109 bytes, `bom=False`
- **Result**: Supported
- **Conclusion**: G0 intent gate passes: no named mechanism is replaced or removed, no user-owned scope decision is pending.
- **Related**: OQ-002, VAL-002, EVD-009

### RISK-002: Wide subagent dispatches stall before writing artifacts

- **Risk**: A subagent given a multi-file contract can spend its whole turn exploring and never write a file; two dispatches (intent audit first attempt, round-1 combined implementer) produced nothing for 15–20 minutes.
- **Likelihood**: Medium
- **Impact**: High (round stalls; no evidence to verify)
- **Evidence**: `list_agents` showed `running` with no files on disk after 20 minutes; the intent audit only landed after the scope was cut to two files.
- **Mitigation**: Dispatch one narrow slice with 1–2 production files and 1 test file; order the agent to write tests first; time-box exploration; verify the first file appears before waiting further.
- **Gate**: G4

### VAL-004: Round-1A simulator slice is supported

- **Target**: round-1A claim batch
- **Method**: Read the diff of `MatchSimulator`/`SimulationOptions`; rebuilt; ran the four new tests and the full suite myself; checked no existing test file changed
- **Evidence examined**: `git diff` (40 inserted lines, no loop reorder); `dotnet build ... -c Release` → 0 errors; `RunXunit ... Tests.Evaluation.DetailedSimulationTests` → `TOTAL 4 PASSED 4 FAILED 0`; full suite → `TOTAL 450 PASSED 450 FAILED 0`
- **Result**: Supported
- **Conclusion**: `RunDetailed` is additive; the 446-test baseline is intact (450 = 446 + 4); the report-equality test pins the two-seat bytes.
- **Related**: EVD-010, EVD-011, ART-003

### VAL-005: Round-1B game contract is supported

- **Target**: round-1B claim batch
- **Method**: Read `GameConfiguration.cs` and its tests; rebuilt; ran the seven tests and the full suite myself; checked the refusal-code tests assert exact codes, not merely that something throws
- **Evidence examined**: exact canonical JSON literal pinned in the test; `dotnet build` 0 errors; `TOTAL 7 PASSED 7`; full suite `TOTAL 457 PASSED 457 FAILED 0`
- **Result**: Supported
- **Conclusion**: The versioned game contract is real and falsifiable; 457 = 446 baseline + 4 RunDetailed + 7 GameConfiguration.
- **Related**: EVD-012, ART-004, SIM-002

### VAL-006: Round-1 seed/plan slice is supported

- **Target**: round-1C claim batch
- **Method**: Read `SeedDomain.cs` and `EvaluationPlanV2.cs`; re-derived the pinned seed fixture independently; rebuilt; ran the five seed tests, the six plan tests and the full suite myself
- **Evidence examined**: independent BigInt derivation matched `[1302179516557818905, 9576118770318801856]`; `TOTAL 5 PASSED 5`, `TOTAL 6 PASSED 6`, full suite `TOTAL 468 PASSED 468 FAILED 0`
- **Result**: Supported
- **Conclusion**: Round 1's contract layer is complete and verified. 468 = 446 + 22 new tests. D1 is Partial: CLI/API still heads-up (SIM-001 acceptance not yet met).
- **Related**: EVD-013, EVD-014, ART-005, SIM-002, EVA-008

### VAL-007: Round-2A opponent population and statistics are supported

- **Target**: round-2A claim batch
- **Method**: Read `OpponentPool.cs` and `BlockStatistics.cs`; checked the holdout ids are disjoint and the archetype test asserts concrete observed counts; rebuilt and ran the pool/stats tests and the full suite myself
- **Evidence examined**: pinned pool hash `f5c1f058e45f21ba360fe9af349e6dc0de960e32130730c70c2ec289a7b44fc1`; observed archetype counts station calls 179 > TAG 85, LAG aggressive 100 > TAG 62, adversarial 123 > TAG 62, tight-passive 50 < TAG 62; `TOTAL 6 PASSED 6`, `TOTAL 4 PASSED 4`, full suite `TOTAL 478 PASSED 478 FAILED 0`
- **Result**: Supported
- **Conclusion**: OPP-001/OPP-003's population layer exists with behavior-level evidence, and EVA-006's estimator is available. The evaluator is the remaining D6/D7 piece.
- **Related**: EVD-015, ART-006, OPP-001, EVA-006

### VAL-008: Round-2B paired evaluator is supported

- **Target**: round-2B claim batch
- **Method**: Read the evaluator's seat builder, paired-run loop and paired-deck assertion; ran the six evaluator tests and the full suite myself; checked the dominated-control sign against the pilot (the first draft had the roles inverted; I corrected it with the pilot evidence and the agent flipped them)
- **Evidence examined**: `dominated-control mean=97.51041666666667 lower=57.95538331320324 upper=139.69557291666663 units=96`; self-comparison exact 0.0 assertions; `TOTAL 6 PASSED 6`; full suite `TOTAL 484 PASSED 484 FAILED 0`
- **Result**: Supported
- **Conclusion**: EVA-001..005 have a working implementation; the gate/verifier and the milestone experiment remain.
- **Related**: EVD-016, EVD-017, ART-007, EVA-002, EVA-005

### VAL-009: Round-3A statistical gate is supported

- **Target**: round-3A claim batch
- **Method**: Read the gate; verified it never reads the stored estimate and recomputes block means from Units; ran the seven gate tests and the full suite myself
- **Evidence examined**: tests poison the stored estimate with 12345 and still reject self-comparison; Bonferroni case expects 0.975; `TOTAL 7 PASSED 7`; full suite `TOTAL 491 PASSED 491 FAILED 0`
- **Result**: Supported
- **Conclusion**: EVA-008/011/013 and the verdict taxonomy are implemented and falsifiable. The independent verifier and the milestone experiment remain.
- **Related**: EVD-018, ART-008, EVA-010

### VAL-010: Round-3B independent verifier is supported

- **Target**: round-3B claim batch
- **Method**: Read the verifier; confirmed it invokes both delegates, compares report hashes and re-derives the gate decision; ran the four verifier tests and the full suite myself
- **Evidence examined**: `TOTAL 4 PASSED 4`; full suite `TOTAL 495 PASSED 495 FAILED 0`; the always-promote test and the tampered-report test both refuse with their named reasons
- **Result**: Supported
- **Conclusion**: EVA-010 is implemented. The last Milestone B piece is the pre-registered control experiment.
- **Related**: EVD-019, ART-009, EVA-010

### OBS-008: The Milestone B holdout confirms the control pair

- **Source**: `docs/goal-tholdem-enhancement/experiments/control-v1/results/` (run 2026-11-10)
- **Fact**: On 288 pre-registered holdout blocks against four frozen compositions, fold-only beat call-any by mean +199.738 bb/100 with lower bound +176.917; the anchor composition was +144.651/+115.038; the gate returned VerifiedImprovement and the independent verifier accepted; forced and tampered controls were refused.
- **Implications**: The evaluator/gate/verifier stack detects a known difference on data fixed before the run. This is evidence about the instrument, not a strategic promotion.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-020, EVD-021, EVD-022, ART-010

### VAL-011: Milestone B exit evidence is met

- **Target**: S1 / Milestone B
- **Method**: Ran the harness myself; inspected every artifact; recomputed the report-file SHA-256; re-ran the harness in a separate process and compared six files; verified the forced/tampered controls; confirmed the full suite
- **Evidence examined**: primary +199.738/+176.917 on 1,152 units; anchor +144.651/+115.038; decision VerifiedImprovement; verifier true; controls false with named reasons; six byte-identical artifacts; suite 495/495
- **Result**: Supported
- **Conclusion**: (a) self-comparison exactly 0.0, (b) a known stronger scripted agent beats a dominated one on pre-registered four-seat data, (c) a forced-false promotion is refused, (d) reports reproduce byte-for-byte. D6 and D7 are Covered; D1 and D11 remain Partial; S2 is not met.
- **Related**: EVD-020..023, ART-010, ART-011, S1

### DEC-004: Accept Milestone B and keep the goal active for Milestones C–F

- **Decision**: Treat S1 as met and this campaign as complete at Milestone B. Do not mark the overarching goal complete; D2–D5, D8–D10, D12 and the full D1/D11 checks remain open.
- **Owner**: agent
- **Status**: approved
- **Rationale**: The specification's own Milestone B exit evidence is reproduced (self-zero, known dominance on pre-registered data, forced promotion refused, byte-identical reports). The remaining milestones are separate dependency-ordered campaigns, not scope exclusions.
- **Alternatives considered**: Continuing directly into equity/policy in the same session was rejected because the context budget would be spent before a second verified milestone; the journal and state preserve the next objective.
- **Related**: VAL-011, EVD-020..023, ART-011

### VAL-012: Round-5A multiway expected pot share is supported

- **Target**: round-5A claim batch (EQ-002, EQ-003)
- **Method**: Read `MultiwayEquity` and the exhaustive oracle; checked the oracle averages all three pairings of every four-card set; ran the five tests and the full suite myself
- **Evidence examined**: sampled 0.79785 vs exhaustive 0.7961475217289171 (delta 0.00170, tolerance 0.02); royal-flush board: win 0.0, tie 1.0, share 0.25 exact; five opponents share 0.5609 with counts summing; `TOTAL 5 PASSED 5`; full suite `TOTAL 500 PASSED 500 FAILED 0`
- **Result**: Supported
- **Conclusion**: N-opponent sampling, card removal and 1/k pot share are real and independently checked. The runtime tool is still heads-up until round 5B.
- **Related**: EVD-024, EVD-025, ART-012, EQ-002, EQ-003

### VAL-013: Round-5B typed equity tool is supported

- **Target**: round-5B claim batch (EQ-001, EQ-002 tool path)
- **Method**: Read `HandCategoryScore`, the tool branch and the catalog schema; ran all 12 equity tests and the full suite myself
- **Evidence examined**: heads-up path matches every legacy field exactly (ExpectedPotShare == Equity, Opponents 1); 3-opponent path maps the multiway estimate; 0/6 opponents throw; catalog exposes optional integer `opponents`; `TOTAL 12 PASSED 12`; full suite `TOTAL 507 PASSED 507 FAILED 0`
- **Result**: Supported
- **Conclusion**: EQ-001 and the EQ-002 tool path are implemented; EQ-002's statistical behaviour was already supported by EVD-024. D3 is Partial: range conditioning (EQ-004), side-pot call EV (EQ-005) and confidence-gated decisions (EQ-006) remain.
- **Related**: EVD-026, EVD-027, ART-013, EQ-001, EQ-002

### VAL-014: Round-6A range-conditioned sampling is supported

- **Target**: round-6A claim batch (EQ-004)
- **Method**: Read the range type and the weighted sampler; checked the 169-class canonical order, per-combo weights, collision filtering and the range-exhausted branch; ran the six tests and the full suite myself
- **Evidence examined**: uniform 0.8245 vs AA-only 0.182375 (delta 0.642125) for KhKd with one opponent at 20,000 samples; all four aces blocked -> InvalidOperationException with range-exhausted; TOTAL 6 PASSED 6; full suite TOTAL 513 PASSED 513 FAILED 0
- **Result**: Supported
- **Conclusion**: EQ-004's range-conditioned sampler exists with the uniform control kept. EQ-005 (side-pot call EV) and EQ-006 (confidence-gated decisions) remain.
- **Related**: EVD-028, EVD-029, ART-015, EQ-004

### VAL-015: Round-6B confidence-gated decisions are supported

- **Target**: round-6B claim batch (EQ-006)
- **Method**: Read `EquityThresholdPolicy`; checked strict-above, strict-below and boundary rules; ran the four tests and the full suite myself
- **Evidence examined**: interval (0.60,0.70)/0.50 -> Call; (0.20,0.30)/0.50 -> Fold; (0.45,0.55)/0.50 -> Uncertain; (0.50,0.60)/0.50 -> Uncertain; threshold 1.5 refused; `TOTAL 4 PASSED 4`; full suite `TOTAL 517 PASSED 517 FAILED 0`
- **Result**: Supported
- **Conclusion**: EQ-006's confidence gate exists. D3 now lacks only EQ-005 (side-pot/contribution-aware call EV).
- **Related**: EVD-030, EVD-031, ART-016, EQ-006

### VAL-016: Anchor-3 equal-budget ablation returns a null, not a learning claim

- **Target**: anchor 3 / round-7 experiment
- **Method**: Ran a 4-task (2 starting genomes x 2 compositions), 4-arm, 8-candidate equal-budget experiment through the four-seat evaluator/gate; inspected every verdict; corrected an over-lenient outcome rule before accepting the result
- **Evidence examined**: 737,280 focal hands; NoSearch 0 verified; Random 1 verified + 3 unsafe; Grid 0 + 3 unsafe + 1 inconclusive; Greedy 0 verified + 1 unsafe + 2 inconclusive; outcome `no-benefit`; report hash `751ca8bcdd3e90be5c6ef39e82f0dc29eca1dac1c2f4440c078bd2d2e7b81d92` equals the file hash
- **Result**: Supported (the null is the finding)
- **Conclusion**: The first mechanical `benefit` was falsified as a credible claim: one Random task win with three anchor regressions is not a proposal-process benefit. No learning-competence claim is made; anchor 3 remains open. The gate's anchor guard rejected the large primary-task gains, which is evidence the guard works.
- **Related**: EVD-032, EVD-033, ART-017, ART-018, META-004

### VAL-017: Round-8 contribution-aware call EV is supported

- **Target**: round-8 claim batch (EQ-005)
- **Method**: Read the calculator; hand-checked the layer arithmetic on the heads-up, side-pot, dead-money, excess-return and future-cost fixtures; ran the six tests and the full suite myself
- **Evidence examined**: pot 170, own 20, seat1 50 live, seat2 100 live, call 80, equities [0.3,0.4] -> effective 80, layers 150/100, contestable 250, EV 5.0, break-even 0.32; `TOTAL 6 PASSED 6`; full suite `TOTAL 531 PASSED 531 FAILED 0`
- **Result**: Supported
- **Conclusion**: EQ-005's calculator exists and is independently checkable. It is not wired into a strategy agent or the runtime tool, so D3 stays Partial: the legacy `StrategyAgent` still compares a hand-category score to pot odds, and range inference from public history is not implemented.
- **Related**: EVD-034, EVD-035, ART-019, EQ-005

### OBS-009: The first equity-aware policy is legal but passive

- **Source**: `Tests.Learning.EquityPolicyAgentTests` run, 12 hands, seed 20261109, 150 samples, default policy
- **Fact**: 22 decisions: 1 Bet, 1 Raise, 1 Call, 9 Check, 10 Fold, 0 AllIn. The policy folds or checks 19 of 22 decisions.
- **Implications**: The conservative `Uncertain -> Fold` rule and wide 150-sample intervals dominate; a 400+ sample budget and/or a less binary uncertainty rule is needed before this policy is a credible search starting point. This is an observation, not a strength claim.
- **Confidence**: Medium (one seed family, 12 hands)
- **Agent**: orchestrator
- **Related**: EVD-037, ART-020, POL-005

### VAL-018: Round-9 equity policy and pot-relative sizing are supported

- **Target**: round-9 claim batch (D4 / POL-005, POL-006)
- **Method**: Read the sizer and agent; ran the five sizing tests, four agent tests and the full suite myself; checked legality/determinism assertions are not weakened
- **Evidence examined**: 25/50/75/100% targets 50/100/150/200; clamp cases; no-live-opponents synthetic observation returns the first legal action; 12-hand 4-seat match with 22 legal decisions; `TOTAL 5 PASSED 5`, `TOTAL 4 PASSED 4`, full suite `TOTAL 540 PASSED 540 FAILED 0`
- **Result**: Supported (for legality, determinism, sizing and opponent-relative thresholds; not for strength)
- **Conclusion**: A new equity-aware policy family exists and the legacy `StrategyAgent` is untouched. D4 is Partial: preflop situation branches need an action history the observation does not carry, postflop street branches are only two-way, and no action trace exists. No strength or EV claim is made.
- **Related**: EVD-036, EVD-037, ART-020, OBS-009

### DEC-005: Approve the additive public-surface update to PlayerObservationTests

- **Decision**: Allow one existing test to gain the two new public property names (`ActionHistory`, `OwnTotalCommitted`) while keeping every other assertion intact.
- **Owner**: agent
- **Status**: approved
- **Rationale**: OBS-003/004 intentionally add public observation members. The test pins the documented public surface, so the contract change must be reflected there; this is a surface update, not a weakened behavioral assertion. The canonical-observation golden is unchanged and independently confirmed by the probe (EVD-038).
- **Alternatives considered**: making the members non-public would hide the data from policies/tools and fail the requirement; leaving the suite red would violate the gate.
- **Related**: EVD-038, EVD-039, OBS-003, OBS-004

### VAL-019: Round-10 observation truthfulness is supported

- **Target**: round-10 claim batch (OBS-003, OBS-004)
- **Method**: Read the additive PlayerObservation diff; ran the four new tests and the full suite; rebuilt and reran the existing ObservationProbe against its pre-change baseline
- **Evidence examined**: totals sum to pot; action history matches the internal log; canonical JSON contains none of the new names; 7 probe lines byte-identical to baseline; hand hash `c953464b...` unchanged; `TOTAL 4 PASSED 4`; full suite `TOTAL 544 PASSED 544 FAILED 0`
- **Result**: Supported
- **Conclusion**: Policies can now see total commitments and action history without changing any agent seed or hand hash. D2 is Partial: OBS-005 (VPIP nullability) and a versioned DecisionContext remain.
- **Related**: EVD-038, EVD-039, ART-021, DEC-005

### DEC-006: Authorize the two VPIP test updates

- **Decision**: Allow the existing strategy-analyzer VPIP assertion to become a measured value + denominator and the prompt analysis-member count to grow from 6 to 8, with no other assertion changed.
- **Owner**: agent
- **Status**: approved
- **Rationale**: The old analyzer assertion pinned the fabricated zero; the prompt contract adds exactly `vpipHands` and `vpipVoluntary`. Both are required by OBS-005, are additive/semantic corrections, and are covered by the new VPIP tests. The canonical report/manifest bytes are untouched.
- **Alternatives considered**: keeping the 6-member prompt would omit the denominator and reintroduce the missingness lie; leaving the suite red would fail the gate.
- **Related**: EVD-040, EVD-041, OBS-005

### VAL-020: Round-11 measured VPIP is supported

- **Target**: round-11 claim batch (OBS-005, DIA-005 partial)
- **Method**: Read the simulator/analysis/prompt diff; ran the five new tests and the full suite; checked the only existing-test edits are the two authorized lines
- **Evidence examined**: 0/10 -> 0.0; 5/10 -> 0.5; no-summary report -> null + 0 counts; prompt `vpip:null` and `vpip:0` with denominators; `TOTAL 5 PASSED 5`; full suite `TOTAL 549 PASSED 549 FAILED 0`
- **Result**: Supported
- **Conclusion**: The VPIP=0 lie is fixed: 0/20 and 0/0 are now distinct states, and the model sees nullability + denominators. D2 is Partial (versioned DecisionContext still absent); D5 remains unstarted.
- **Related**: EVD-040, EVD-041, ART-022, DEC-006

### VAL-021: Round-12 preflop/call-EV integration is supported

- **Target**: round-12 claim batch (POL-002 partial, EQ-005 integration)
- **Method**: Read the bridge, classifier and policy changes; ran the three test classes and the full suite myself; checked the policy no longer compares a raw pot-odds scalar
- **Evidence examined**: side-pot bridge effective 80 / contestable 280 / EV -10 / break-even 0.285714; classifier fixtures 4/4; 12-hand probe classifications 10 postflop, 3 unopened, 9 facing limp (no open/three-bet seen, synthetic only); post-change distribution Fold 10, Check 9, Call 1, Bet 1, Raise 1; `TOTAL 3 PASSED 3`, `TOTAL 4 PASSED 4`, `TOTAL 4 PASSED 4`, full suite `TOTAL 556 PASSED 556 FAILED 0`
- **Result**: Supported
- **Conclusion**: The equity policy now uses side-pot-aware break-even and preflop situation margins. Per-layer equity is still a uniform repetition, and the real sample did not exercise open/three-bet branches. No strength claim; anchor 3 remains the round-7 null.
- **Related**: EVD-042, EVD-043, ART-023, POL-002

### VAL-022: Round-13 plan persistence is supported

- **Target**: round-13 claim batch (D1 prerequisite, EVA-008)
- **Method**: Read the strict parser; ran the five tests and the full suite; checked round-trip hash, refusal codes and the semantic-change hash distinction
- **Evidence examined**: round-trip hash `baf6dddd6ad508775483a71c65d1b2f997a424d587de602598bbe93b7e3a8973`; `plan-not-canonical`, `plan-shape-invalid`, `plan-seed-mismatch`, `plan-not-json`; `TOTAL 5 PASSED 5`; full suite `TOTAL 561 PASSED 561 FAILED 0`
- **Result**: Supported
- **Conclusion**: A verifier/CLI can reconstruct the pre-registered plan from disk and recompute its hash; the content address, not the parser, distinguishes semantic changes. The CLI command is the next slice.
- **Related**: EVD-044, EVD-045, ART-024

### VAL-023: Round-13 evaluate4 CLI is supported

- **Target**: round-13b claim batch (D1 CLI slice)
- **Method**: Read the command and dispatch wiring; ran the five CLI tests and the full suite myself; checked the unknown-pool/unknown-composition/malformed-plan refusals and that no pointer is written
- **Evidence examined**: planHash `63eb2c96...`, 4 units, report `report-920f9dcb.json`; anchor decision `Unsafe/unsafe-anchor-regression` with `verifier:verified`; exit 4 codes `unknown-opponent-pool`, `evaluation-plan-invalid`, `plan-not-json`; `TOTAL 5 PASSED 5`, full suite `TOTAL 566 PASSED 566 FAILED 0`
- **Result**: Supported
- **Conclusion**: The four-seat evaluator/gate/verifier is reachable from the CLI with a persisted pre-registered plan, additively. D1 remains Partial: `train` still defaults to heads-up, so SIM-001's training acceptance is not met.
- **Related**: EVD-046, EVD-047, ART-025

### OBS-010: The v1 equity-policy artifact omits the preflop raise margin

- **Source**: `docs/goal-tholdem-enhancement/round-14/implementation-equity-policy-artifact.md`; `EquityPolicyJson`
- **Fact**: The v1 canonical equity-policy JSON does not carry `PreflopRaiseMargin`; `Write` refuses a policy whose margin is non-default rather than silently dropping the field. Default-margin policies round-trip exactly.
- **Implications**: A policy family artifact that cannot represent a live decision field is incomplete for arbitrary policies. The upcoming promotion experiment uses the default margin, so it is unaffected; a v2 schema should add the field (and a versioned parse).
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-048, ART-026, OQ-003

### OQ-003: When should the equity-policy artifact schema gain preflopRaiseMargin?

- **Question**: Extend the artifact to v2 with `preflopRaiseMargin`, or keep v1 default-only and express non-default margins as separate policy ids?
- **Needed evidence**: Whether the next experiment or a meta-proposer needs non-default margins; the cost of a versioned parser.
- **Priority**: P1
- **Raised by**: Round 14
- **Related**: OBS-010, ART-026

### VAL-024: Round-14 equity-policy artifact and CLI kind are supported

- **Target**: round-14 claim batch (POL-015 partial, D1 CLI)
- **Method**: Read the JSON/store/factory; ran the three JSON tests, six CLI tests and full suite myself; checked the strategy path and no-pointer rule remain
- **Evidence examined**: default policy hash `760ac54c...`; equity-policy report `challengerPolicyHash 9c8924...`, report hash `05ca3b3a...`; strategy report `report-920f9dcb.json` unchanged; no pointer; `TOTAL 3 PASSED 3`, `TOTAL 6 PASSED 6`, full suite `TOTAL 570 PASSED 570 FAILED 0`
- **Result**: Supported
- **Conclusion**: The CLI can evaluate a persisted equity-policy challenger against a strategy incumbent with content-addressed provenance. The artifact omits `PreflopRaiseMargin` (OBS-010) and the next step is the actual pre-registered experiment, not a strength claim.
- **Related**: EVD-048, EVD-049, ART-026, OBS-010

### HYP-002: The equity-aware policy beats the legacy policy on the pre-registered holdout

- **Claim**: The default `EquityPolicyAgent` has a positive paired four-player bb/100 lower bound over the default `StrategyAgent` on the holdout opponent pool and does not regress on the anchor composition.
- **Supporting OBS**: OBS-009 (the policy is legal but passive), EVD-042/043 (call-EV and preflop integration)
- **Why it matters**: This is the first strategic-improvement test of the new policy family under the v2 gate; a null is informative.
- **Falsification strategy**: Run the pre-registered plan (16 holdout blocks, 2 compositions, 2 hands per seat match, margin 10 bb/100, 0.95 confidence, anchor tolerance 5) through `evaluate4`; a lower bound at or below the margin falsifies the claim.
- **Status**: Falsified
- **Confidence**: High (observed)
- **Related**: ART-026, ART-027, VAL-025, EVA-009

### VAL-025: HYP-002 is falsified by the pre-registered holdout

- **Target**: HYP-002 / equity-policy-v1 experiment
- **Method**: Wrote the preregistration before the run, executed `evaluate4` through the Host CLI with the anchor plan, inspected the decision/verification and checked the report file hash
- **Evidence examined**: plan `3af4d905...`, anchor `b67d7cad...`, policy `760ac54c...`, incumbent `f100e793...`; primary mean **-93.83 bb/100**, report lower **-454.76**, 32 units; gate **Unsafe / unsafe-anchor-regression** (anchor lower **-961.73**); verifier `verifier:verified`; report hash `4c18e8bc...` equals the file hash
- **Result**: Falsified
- **Conclusion**: The default equity-aware policy is worse than the legacy heuristic on this pre-registered holdout and regresses on the anchor. No strategic-improvement claim. The passive policy (OBS-009) and the single-run sample are the likely causes; this is a real negative result, not a harness failure.
- **Related**: EVD-050, EVD-051, ART-027, HYP-002

### DEC-007: Conclude the campaign without marking the goal complete

- **Decision**: Consolidate at the 12-round goal limit and leave the goal active; do not mark it complete.
- **Owner**: agent
- **Status**: approved
- **Rationale**: S1 (Milestone B) is met and R1 is substantially delivered, but S2 is not: D5, D9, D10 and D12 have no evidence, D1 lacks the four-seat training path, and anchor 3 is a recorded null. Marking complete would violate the user's delivery condition.
- **Alternatives considered**: claiming R1 as full completion, rejected because the legacy equity path and four-seat training surface are unfinished; continuing another implementation round, impossible within the goal round limit.
- **Related**: final-report.md, VAL-025, DEC-004

### DEC-008: New campaign segment for the user's items 1, 2 and 3

- **Decision**: Continue the same goal with a new segment: (1) four-seat training/promotion path, (2) a richer diagnosis-driven proposer and an anchor-3 retry, (3) D5 decision-time diagnostics followed by LLM attribution with an explicit model budget.
- **Owner**: user
- **Status**: approved
- **Rationale**: The user explicitly directed `/stretch-goal-harness go for 1 2 and 3`. The goal was extended to 24 rounds; dependency order is 1, then diagnostics (part of 3), then the proposer (2), then LLM attribution (3b), because a diagnosis-driven proposer cannot be evaluated before decision-time labels exist.
- **Alternatives considered**: doing item 2 before diagnostics would repeat the weak arbitrary-mutation search that already returned a null.
- **Related**: final-report.md, goal.md, TODO list

### VAL-026: Round-17 four-seat training loop is supported

- **Target**: round-17 claim batch (item 1, D1/SIM-001)
- **Method**: Read the loop; ran the four tests and the full suite myself; observed the inert-selection layout, the repeat hash, and the absence of v1 artifacts
- **Evidence examined**: inert challenger mean 0 -> rejected-selection, champion `tholdem-strategy-v1-f100e793` unchanged; repeat `runHash 4064f00c...`; files only under `four-seat/{strategies,reports}`; no v1 `active-champion.json`/`ledger.json`/`strategies`/`evaluations`; `TOTAL 4 PASSED 4`, full suite `TOTAL 574 PASSED 574 FAILED 0`
- **Result**: Supported
- **Conclusion**: The library-level four-seat training/promotion path works and is isolated from v1. The CLI `train4` command is the remaining half of item 1.
- **Related**: EVD-052, EVD-053, ART-028, SIM-001

### VAL-027: Round-17 `train4` CLI is supported

- **Target**: round-17b claim batch (item 1, SIM-001 CLI acceptance)
- **Method**: Read the command; ran the CLI test and the real Host CLI myself; inspected the files and the v1 pointer
- **Evidence examined**: `iteration=1 champion=... challenger=... outcome=rejected-selection ... meanBbPer100=0.00`; `four-seat/reports/iter-1-selection.json`, two strategy files; `<data>/active-champion.json` false; `TOTAL 1 PASSED 1`, full suite `TOTAL 575 PASSED 575 FAILED 0`
- **Result**: Supported
- **Conclusion**: Item 1's CLI surface is delivered: a four-seat training/promotion command exists, promotes only through the v2 gate, and is isolated from v1. SIM-001's stronger 'default environment' wording remains Partial because the legacy `train`/`evaluate` commands still default to heads-up.
- **Related**: EVD-054, EVD-055, ART-029, SIM-001

### VAL-028: Round-18 diagnostics repair is supported

- **Target**: round-18 claim batch (DIA-004, DIA-005)
- **Method**: Read the extractor/property diff; ran the three new tests, the refinement regression class and the full suite myself
- **Evidence examined**: new hand fold-to-raise 1/1; existing fixture 1/3 unchanged (canonical JSON identical); empty extract six signals Sample 0/Count 0/Observed false; checked hand aggression Sample 3/Count 0/Observed true; `TOTAL 3 PASSED 3`, `TOTAL 8 PASSED 8`, full suite `TOTAL 578 PASSED 578 FAILED 0`
- **Result**: Supported
- **Conclusion**: Fold-to-raise no longer depends on the immediately preceding action, and missingness is explicit. D5 moves from None to Partial; DIA-001/002/003 still need decision-time EV/counterfactuals.
- **Related**: EVD-056, EVD-057, ART-030

### CAP-001: Live DeepSeek is configured but unexercised

- **Capability**: live model for LLM attribution (item 3b).
- **Checks**: `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, `DEEPSEEK_API_KEY`, `OLLAMA_HOST` etc. absent from the process environment; `.secrets/deepseek.env` exists and contains `DEEPSEEK_API_KEY` and `DEEPSEEK_BASE_URL` (names only, values never printed); `THOLDEM_LIVE_MODEL=1` is required by `LiveModelConfiguration`; no `ollama`/`llm`/`aider` CLI.
- **Result**: configured, not exercised. No model call and no LLM claim were made because no budget or attribution protocol was provided.
- **Related**: DEC-008, RISK-006

### RISK-006: Item 3b has no experiment budget or protocol

- **Risk**: Without a model budget, a pre-registered attribution task set, and an equal-budget control, an LLM claim would be unfalsifiable.
- **Mitigation**: Recorded CAP-001 and left 3b unclaimed; the next campaign must supply a budget and reuse the equal-budget runner shape.
- **Related**: CAP-001

### VAL-029: Round-19 diagnosis-driven proposer is supported

- **Target**: round-19 claim batch (item 2 adapter)
- **Method**: Read the adapter; ran the four tests and the full suite myself; checked the exact delta and fallback
- **Evidence examined**: fold-to-raise 10/10 -> call 0.45 to 0.37 only; empty and 3/3 fallback record-equal to `DeterministicStrategyProposer`; repeated proposals equal; `TOTAL 4 PASSED 4`, full suite `TOTAL 582 PASSED 582 FAILED 0`
- **Result**: Supported
- **Conclusion**: A measured diagnosis can now drive an `IStrategyProposer`. The four-seat loop does not yet capture hand records to feed it, so no new anchor-3 retry was run.
- **Related**: EVD-058, EVD-059, ART-031

### VAL-030: Round-20 diagnosis-driven four-seat proposals are supported

- **Target**: round-20 claim batch (item 2 wiring)
- **Method**: Read the loop/capture diff; ran the six four-seat tests, the real `train4 --diagnosis` CLI, and the full suite myself
- **Evidence examined**: injected fold 10/10 -> challenger callThreshold 0.37; capture signatures samples 1,1,1,3,2,0 and `iter-1-signatures.json`; CLI files: selection + signatures reports, two strategies; v1 pointer false; deterministic default runHash unchanged; `TOTAL 6 PASSED 6`, full suite `TOTAL 584 PASSED 584 FAILED 0`
- **Result**: Supported
- **Conclusion**: A measured diagnosis now reaches the four-seat challenger. The capture is a separate 4-seat match; the equal-budget diagnosis-vs-controls comparison and a new anchor-3 verdict are still not run.
- **Related**: EVD-060, EVD-061, ART-032

### OBS-012: Diagnosis and deterministic four-seat pilots both promoted nothing

- **Source**: `docs/goal-tholdem-enhancement/experiments/diagnosis-pilot-v1/` (`off.log`, `on.log`)
- **Fact**: With seed 1099511627776, 6 iterations, selection 8x4, holdout 8x4, margin 5: deterministic arm 0 promotions (iteration 4 holdout no-improvement, +9.06); diagnosis arm 0 promotions (iteration 6 holdout Unsafe anchor regression, +45.94). Champion unchanged in both; no v1 pointer.
- **Implications**: The wiring runs, but the four-parameter family plus a 20-hand diagnosis capture does not produce a verified promotion in six iterations. This is a pilot, not an equal-budget comparison; no proposer-strength claim.
- **Confidence**: High (observed logs)
- **Agent**: orchestrator
- **Related**: EVD-062, ART-033, VAL-031

### VAL-031: The diagnosis pilot is a null, not a proposer claim

- **Target**: diagnosis-pilot-v1
- **Method**: Ran both arms through the `train4` CLI with the same seed and plan; inspected the logs and the final champion
- **Evidence examined**: 6 iterations per arm; 0 promotions; deterministic holdout no-improvement +9.06; diagnosis holdout Unsafe +45.94; logs persisted; no v1 pointer
- **Result**: Supported (as a null pilot)
- **Conclusion**: The diagnosis path is wired but this pilot does not show it is better. A controlled, pre-registered equal-budget comparison remains open.
- **Related**: OBS-012, ART-033

### OQ-004: Will the operator approve the 16-request LLM attribution budget?

- **Question**: approve 4 tasks x 4 proposals = 16 live DeepSeek requests, with no repair retries, under the pre-registered outcome rule?
- **Needed evidence**: explicit budget/approval; the key is configured but has never been exercised.
- **Priority**: P0 for item 3b
- **Raised by**: Round 20
- **Related**: CAP-001, RISK-006, `experiments/llm-attribution-preregistration.md`

### HYP-003: The diagnosis-driven proposer beats the deterministic proposer at equal budget

- **Claim**: Over four starting genomes and four iterations each, the diagnosis arm produces at least two verified promotions, zero Unsafe verdicts, and strictly more promotions than the deterministic arm.
- **Supporting OBS**: EVD-060 (diagnosis wiring), EVD-062 (short pilot was null)
- **Falsification strategy**: Pre-registered `experiments/diagnosis-ablation-v1/preregistration.md`; run both arms under the same seed and plans.
- **Status**: Falsified
- **Confidence**: High (observed)
- **Related**: EVD-063, VAL-032, ART-035

### OBS-013: The diagnosis arm was less safe and no more successful

- **Source**: `experiments/diagnosis-ablation-v1/results/report.json`, `run.log`
- **Fact**: Deterministic 0/16 promotions with 4 Unsafe; diagnosis 0/16 with 7 Unsafe; outcome `no-benefit` under the pre-registered rule.
- **Implications**: The diagnosis proposer, as wired, does not improve the proposal process and worsens anchor safety on this budget. Anchor 3 remains null.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: HYP-003, EVD-063

### CAP-002: Live DeepSeek was exercised successfully

- **Capability**: live model proposal generation.
- **Checks**: 16 requests via `.secrets/deepseek.env` (values never printed); 16/16 responses parsed by `LlmProposalParser`; no transport refusal.
- **Result**: available and exercised; the 16-request budget was spent exactly once.
- **Related**: CAP-001, EVD-064

### VAL-032: The diagnosis equal-budget ablation is falsified

- **Target**: HYP-003 / `diagnosis-ablation-v1`
- **Method**: Wrote the preregistration first, ran both arms via the new harness, inspected the report
- **Evidence examined**: prereg hash `fa312c5d...`; off 0/16 (4 Unsafe), diagnosis 0/16 (7 Unsafe); outcome `no-benefit`; lead no promotion
- **Result**: Falsified
- **Conclusion**: The diagnosis proposer does not beat the deterministic proposer at this budget; no anchor-3 claim.
- **Related**: EVD-063, ART-035, HYP-003

### VAL-033: The LLM pilot is a gate-null, not an attribution result

- **Target**: `llm-attribution-v1`
- **Method**: Ran 16 live requests, parsed every response, evaluated each proposal through the gate
- **Evidence examined**: 16/16 parsed; 0 VerifiedImprovement, 8 Rejected, 8 Unsafe; means -875.31..690.94; no control arm on this candidate budget
- **Result**: Supported as a pilot null
- **Conclusion**: The live proposal process is reachable and parseable, but no proposal cleared the gate. An equal-budget control comparison remains unrun; no LLM claim.
- **Related**: EVD-064, ART-036, CAP-002

### OBS-014: No proposal arm produced a verified improvement in the equal-budget comparison

- **Source**: `experiments/proposal-comparison-v1/results/report.json`, `run.log`
- **Fact**: LLM 0 verified / 11 Unsafe; random 0/15; grid 0/15; greedy 0/7; nosearch 0/0. Outcome `no-benefit` under the pre-registered rule.
- **Implications**: The LLM arm is not better than the controls on this small budget and is the least safe. No attribution claim.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-066, ART-037, VAL-034

### VAL-034: The LLM vs control comparison is a null

- **Target**: proposal-comparison-v1
- **Method**: Pre-registered the rule, reused the 16 stored LLM proposals, generated four control arms on identical plans, ran the harness
- **Evidence examined**: prereg `f91724dd…`; LLM 0/11; random 0/15; grid 0/15; greedy 0/7; nosearch 0/0; outcome `no-benefit`
- **Result**: Supported as a null
- **Conclusion**: No arm cleared the gate; the LLM proposal process has no demonstrated advantage. Anchor 3 and R3 remain null.
- **Related**: OBS-014, EVD-066, ART-037

### OBS-015: The exploratory v2 diagnosis run lacked an on-disk pre-registration

- **Source**: `experiments/diagnosis-ablation-v2/` vs `diagnosis-ablation-v2r/`
- **Fact**: The first v2 run started before `preregistration.md` existed; its result (0/48 both arms, 7 vs 20 Unsafe) is reported as exploratory only. A clean rerun with the hash `15d2945b…` written first reproduced the same totals.
- **Implications**: v2 cannot be cited as pre-registered; v2r can. Both are null.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-067, VAL-035, ART-038

### VAL-035: The v2r diagnosis replication is a pre-registered null

- **Target**: diagnosis-ablation-v2r
- **Method**: Hashed the preregistration before the run; ran the harness; inspected totals and per-arm Unsafe counts
- **Evidence examined**: prereg `15d2945b…`; off 0/48 (7 Unsafe), on 0/48 (20 Unsafe); outcome `no-benefit`
- **Result**: Supported as a null
- **Conclusion**: The diagnosis proposer does not beat deterministic at this power and is less safe. Anchor 3 remains null.
- **Related**: EVD-067, ART-038, OBS-015

### OBS-016: The four-parameter family contains at least one gate-passing improvement

- **Source**: `experiments/family-probe-v1/results/probe.json`
- **Fact**: Of 144 grid genomes, one — raise 0.95, call 0.20, bluff 0.40, sizing 0.75 — cleared `VerifiedImprovement` against the default (mean +150.625, lower +71.452). The top-mean candidate (raise 0.40, call 0.40, bluff 0.20, sizing 1.25) was `Unsafe` at +1646.25 mean.
- **Implications**: The family is not the sole bottleneck; the search/proposal process is. A proposer that can reach this region could in principle earn a verified promotion, but this probe is not evidence that any proposer does.
- **Confidence**: High (observed)
- **Agent**: orchestrator
- **Related**: EVD-068, ART-039, VAL-036

### VAL-036: The family probe is an existence result, not a learning claim

- **Target**: family-probe-v1
- **Method**: Pre-registered grid/plan, ran all 144 candidates, inspected the verified and top rows
- **Evidence examined**: 1 verified candidate at +150.625 mean / +71.452 lower; top mean candidate Unsafe; 143 non-verified
- **Result**: Supported as an existence result
- **Conclusion**: The family can represent a gate-passing improvement over the default. Whether a proposal process finds it is untested; anchor 3 remains null.
- **Related**: EVD-068, ART-039, OBS-016

### OBS-017: The deterministic control reaches a verified proposal; diagnosis does not

- **Source**: `experiments/reachability-v1/results/report.json`
- **Fact**: 8 chained proposals from the default, fixed default incumbent, 8-block holdout: deterministic 1/8 verified (iteration 7, mean +66.09375, lower +11.669); diagnosis 0/8 (7 Unsafe, 1 Rejected, strongly negative means); random/grid/nosearch 0.
- **Implications**: The diagnosis proposer is worse than a deterministic control here; the family can be climbed by a simple step on a favorable plan. This is a single-plan reachability pilot, not the four-task protocol.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-069, ART-040, VAL-037

### VAL-037: The reachability probe does not support a learning claim

- **Target**: reachability-v1
- **Method**: Pre-registered arms/budget, ran the probe, inspected the verified and diagnosis rows
- **Evidence examined**: deterministic 1/8 at +66.09/+11.67; diagnosis 0/8 with 7 Unsafe; controls 0
- **Result**: Supported as a null for diagnosis
- **Conclusion**: No diagnosis advantage; a positive control signal exists for deterministic on one plan. Anchor 3 remains null.
- **Related**: OBS-017, EVD-069, ART-040

### VAL-038: The signature-guarded proposer is supported

- **Target**: round-26 claim batch
- **Method**: Read the class; ran the four tests and the full suite myself
- **Evidence examined**: call 0.45 -> 0.43 on 10/10 fold-to-raise; empty/below-floor/inert cases record-equal to deterministic; repeats equal; `TOTAL 4 PASSED 4`, full suite `TOTAL 588 PASSED 588 FAILED 0`
- **Result**: Supported
- **Conclusion**: The conservative overlay exists and is deterministic. Whether it beats controls is the v2 comparison's question.
- **Related**: EVD-070, ART-041

### OBS-018: The signature-guarded arm is no better than deterministic and less safe

- **Source**: `experiments/proposal-comparison-v2/results/report.json`
- **Fact**: Guarded 0 verified / 22 Unsafe; deterministic 0/15; random 0/32; grid 0/28; nosearch 0/0. Outcome `no-benefit`.
- **Implications**: The 0.02 signature overlay does not rescue the diagnosis path; more proposals mostly add unsafe cases. No learning claim.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: EVD-071, ART-042, VAL-039

### VAL-039: The guarded comparison is a null

- **Target**: proposal-comparison-v2
- **Method**: Pre-registered arms/budget, ran the harness, inspected totals
- **Evidence examined**: guarded 0/22, deterministic 0/15, random 0/32, grid 0/28, nosearch 0/0; outcome `no-benefit`
- **Result**: Supported as a null
- **Conclusion**: No arm produced a verified improvement; the guarded mechanism is worse than the deterministic control here. Anchor 3 remains null.
- **Related**: OBS-018, EVD-071, ART-042

### TODO-001: Establish baseline test count and commit hash evidence

- **Task**: Run the full Tholdem suite through `RunXunit`, record pass/fail counts and the DLL hash.
- **Priority**: P0
- **Depends on**: nothing
- **Status**: Done
- **Related**: TST-001, EVD-006, EVD-007, EVD-008
