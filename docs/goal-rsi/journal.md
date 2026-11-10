# Goal Journal — Wip.Tholdem mini-RSI

> Append-only. Entry templates: OBS / HYP / VAL / DEC / RISK / OQ / TODO, IDs per kind.

---

<!-- New entries go below this line -->

### DEC-001: Training job execution is a host-owned single-consumer queue

- **Decision**: `POST /api/training` validates and enqueues a job on a `Channel<TrainingWorkItem>` consumed by one `BackgroundService`. The request thread only creates the record; the loop runs on the queue thread.
- **Rationale**: The spec requires the request thread to stay free, progress to be observable, and one job at a time. A single consumer makes the one-at-a-time rule structural rather than a lock convention, and gives cancellation one owner.
- **Alternatives considered**: (a) `Task.Run` per request — rejected: no queue, harder to prove one-at-a-time; (b) run inline on the request thread — rejected by the spec.
- **Related**: goal.md R1

### DEC-002: Promotion is a second, read-back phase; the job record only supplies expectations

- **Decision**: `POST /api/training/{jobId}/promote` never trusts the job's cached decision. It re-reads the persisted evaluation report, re-reads both strategy artifacts, recomputes the report with `MatchEvaluator` from the stored seeds and artifacts, compares the recomputed hash with the persisted hash and with the hash recorded when the iteration completed, then re-runs `StatisticalPromotionPolicy` + `PromotionVerifier` with the job's criteria.
- **Rationale**: A coherently edited report would pass a verifier that only reads the file. Recomputing from the seed and the frozen agents makes a forged report fail unless the attacker also forges the artifacts, and the artifact ids are content hashes.
- **Alternatives considered**: trusting a store boolean — rejected as A8; only verifying the file — rejected because a coherent edit passes.
- **Related**: goal.md R2, A8

### DEC-003: Meta promotion changes only the proposer pointer, never the strategy champion

- **Decision**: Meta-loop episodes run inside `<data>/meta/scratch/...`; a meta promotion writes `<data>/meta/active-proposer.json` (and a hash-chained meta ledger) but never `active-champion.json`. The strategy champion changes only through the training gate or the explicit training promote endpoint.
- **Rationale**: One champion pointer with two independent graders would let a meta win overwrite a strategy verdict. Separating the pointers keeps each promotion attributable to exactly one gate.
- **Alternatives considered**: let meta promotion also train a final champion in the main directory — rejected: mixes two gates and two ledgers.
- **Related**: goal.md R3, A3, A6

### DEC-004: Meta seeds use dedicated derivation streams and are checked disjoint in code and test

- **Decision**: inner training seeds use `TrainingSeedHandBase=1000`, stream 1; inner evaluation seeds use `EvaluationSeedHandBase=5000`, stream 3; meta episode seeds use hand base 20000, stream 7; meta comparison seeds use hand base 30000, stream 7; fresh-holdout seeds use hand base 40000, stream 11. The meta loop asserts the four values it uses for an episode are pairwise distinct.
- **Rationale**: The spec requires meta seeds disjoint from training and evaluation seeds with a documented derivation. Hashes make collisions negligible; an explicit assertion turns that from a probability statement into a refusal.
- **Related**: goal.md R3, A2, A15

### DEC-005: The frozen manifest hashes both canonical source and live behaviour probes

- **Decision**: `FrozenComponents.Capture()` hashes (a) the canonical source text of `EvaluationCriteria.cs`, `PromotionPolicy.cs`, `PromotionVerifier.cs`, `MatchEvaluator.cs` and `SeedDerivation.cs`, and (b) a deterministic behavioural probe set that runs the verifier, the policy, the evaluator with a fixed stub pair and the seed derivation with fixed vectors. The pinned manifest is a versioned constant. When source files are unavailable the source fields are skipped and the manifest reports `sourcePinned=false`; behaviour probes are always required.
- **Rationale**: A source-only hash cannot be checked from a deployed build without source, and a behaviour-only hash misses constant changes that the probes do not touch. Both together cover drift in the dev loopback environment and still fail closed on behaviour everywhere.
- **Residual**: an attacker who edits the frozen source and also edits the pinned constant, then rebuilds, defeats the manifest. No signature chain exists in this delivery; stated in the threat model (A14).
- **Related**: goal.md R3, A14

### TODO-001: Round 1 — training API, server-verified promote, meta-loop core, UI, tests

- **Task**: implement Parts 1–2 and the test suites; keep the 275 baseline tests green.
- **Priority**: P0
- **Status**: In progress
- **Related**: DEC-001…005

### OBS-001: The shipped proposer is behaviourally inert against the baseline in observed runs

- **Observation**: two default-champion jobs (24 and 50 evaluation seeds, 10 hands per match) reported `meanBbPer100 = 0`, `lowerBoundBbPer100 = 0`, `lcb-below-margin`. The ±0.02 nudges changed bluff frequency or sizing, which did not change any action against the fixed baseline on the sampled deals.
- **Evidence**: real loopback runs `train-8839fd176593f4cb` and `train-3bfc0101c7bd3a3a`; also the pre-existing `known-limits.md` note "mean bb/100 = 0.000 observed".
- **Consequence**: a real promotion demo needs an explicit `strategyId` whose thresholds are close to an action boundary. The 0.70/0.70 artifact plus 50 evaluation seeds produced `mean 1.05`, `LCB 0.321` and a promoted verdict (`train-dbadcdd70b0c15a2`). The docs state this as exercising the promote path, not as evidence of skill discovery.
- **Related**: RISK-003, A1, A7

### VAL-001: The pre-existing 275 tests stay green after the loop/ledger/manifest changes

- **Target**: Learning and Web changes before the new suites were added.
- **Method**: rebuilt `WIP/Wip.Tholdem.Tests` Release and ran the supplementary runner myself.
- **Result**: `TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0`, exit 0. No existing expectation was edited; the only compile-visible change is `TrainingIteration.Report` becoming nullable (null only for the new inert-proposal path), which produces nullable warnings in existing tests but no behavioural change.
- **Related**: EVD-003, R6

### OBS-002: The meta-loop refuses the default trajectory as a self-comparison

- **Observation**: a real meta-run with default options (2 meta-iterations, 2 meta seeds, 1 inner iteration) ended both iterations as `champion-vs-itself`: with no inner promotion, both proposers left the champion at the same genome. Refusing before evaluation is correct (A5) and is the honest "no signal" result.
- **Evidence**: `.tmp/tholdem-verify/MetaProbe` run; later the small `/api/rsi` job test asserts completion without a pointer change.
- **Related**: RISK-004, A5

### RISK-003: Promotion demonstrations require a non-default starting artifact

- **Risk**: the default v1 champion plus the deterministic ±0.02 proposer produces zero-effect evaluations, so the UI cannot show a success without a deliberate starting point.
- **Impact**: Medium for the demo, Low for the gate. The gate itself is correct; the search space is weak.
- **Mitigation**: expose and use `strategyId`, document the observed 0.000 result, and state in `rsi.md §9` that this is not skill discovery.
- **Related**: OBS-001

### RISK-004: Meta promotion success is only proven at the library seam

- **Risk**: the HTTP meta promote success path is not exercised by a real eligible run; real small runs are correctly refused as self-comparisons. The library tests prove eligibility, the document re-verification and the no-automatic-pointer rule with injected episodes/evaluators.
- **Impact**: Low; the endpoint shares `MetaDecisionVerifier` and the document store with the tested library path, and refuses rather than promotes when unsure.
- **Mitigation**: documented as a residual in the threat model / final report; a future seeded fixture could exercise the HTTP success path.
- **Related**: OQ-001

### OQ-001: How to produce a reproducible eligible meta-run without injected fakes?

- **Question**: the meta-loop needs inner promotions to distinguish proposers, and the default proposer is inert. Starting from a 0.70/0.70 artifact may make episodes promote; a future round could search a documented, pre-registered configuration instead of injecting fake episodes.
- **Status**: open; not a blocker for the bounded-loop deliverable.
### VAL-002: The 317-test suite is green and the UI works end-to-end

- **Target**: full tree after the training API, meta-loop, controls and Training tab.
- **Method**: rebuilt Release and ran the supplementary runner over the whole assembly; ran a real copied host and a headless Playwright capture of the Training tab (start → complete → promote; start → running → cancel; 390 px).
- **Result**: `TOTAL 317 PASSED 317 FAILED 0 SKIPPED 0` (275 pre-existing + 42 new). Browser: `status=Complete · persist · iteration 1 of 1`, `promote_visible=1`, `Already promoted: tholdem-strategy-v2-f7a0e101 (server verified)`, `running_status=Running · self-play · iteration 2 of 50`, `cancelled_status=Cancelled`, `console_errors=0`. Two pre-existing viewer tests initially caught UI regressions (a 42 rem training-table width and the 1–4 hinted range); both were fixed in the UI without changing the tests.
- **Related**: EVD-009, EVD-010, EVD-011
### VAL-003: Final gates and the mini-RSI UI

- **Target**: final tree.
- **Method**: full suite run; solution Release build; architecture runner; `verify-solution-coverage.ps1`; `HarnessSync --check`; a headless-browser mini-RSI run (form → start → complete).
- **Result**: `TOTAL 317 PASSED 317 FAILED 0`; solution build `0 Error(s)`; architecture `TOTAL 4 PASSED 4`; coverage `wip_project_count=48 solution_wip_project_count=48 missing=0 extra=0 solution_coverage=ok`; HarnessSync exit 0. Mini-RSI UI: `status=complete · persist · iteration 1 of 1 · incumbent tholdem-proposer-v1-24c2109a`, message `Run complete: no eligible proposer. The strategy champion is unchanged.`, `promote_visible=0`, `console_errors=0`.
- **Related**: EVD-014, EVD-018