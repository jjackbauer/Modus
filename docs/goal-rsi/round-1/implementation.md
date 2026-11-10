# Round 1 — Training API, mini-RSI core, viewer tab, controls

Status: complete for R1–R7. Verified by 317/317 tests, 11 mutation controls (red-then-green, restored by SHA-256), the integration gates below, and a real headless-browser Training-tab run.

## Delivered

### Learning (`WIP/Wip.Tholdem.Agents/Learning`)

- `TrainingLoop`: phase observer (`ITrainingObserver`), optional injected proposer/guard,
  `StartingStrategyId`, cancellation checks at every phase boundary, inert proposals recorded without
  an evaluation, hash-chained ledger writes, champion pointer carrying the report hash.
- `LedgerChain` / `TrainingLedger`: append-only, tamper-evident hash chain with legacy-prefix upgrade.
- `EvaluationReportJson`: canonical read-back used by promote.
- `ProposerGenome`, `PromptTemplateRenderer`, parameterised `DeterministicStrategyProposer`,
  `MetaArtifact`, `DeterministicMetaProposer`.
- `FrozenComponents`: canonical-source + behaviour-probe manifest, pinned at
  `95e010703d8ef7cd4d7cc2fae4ca839f7dd80b4c99b7645c7ac3d111ca748a5d`.
- `MetaSeedDerivation`, `MetaStatistics`, `HitRateCalculator`, `MetaDecisionVerifier`,
  `MetaEvaluationDocument`, `MetaEvaluationJson`, `MetaLoop`, `MetaStore`.
- Budgets (episode count, per-episode evaluations, wall clock), cancellation, fresh-holdout ratchet,
  self-comparison refusal, identical/inert proposer refusal.

### Web (`WIP/Wip.Tholdem.Web/Training`)

- `JobScheduler` + `BackgroundJobWorker` (single-consumer queue, one job per process).
- `TrainingJobValidator` (bounds, weak-seed guard/cap, deterministic options hash/job id).
- `TrainingSeedRegistry` (persisted seed-reuse refusal).
- `TrainingJob`, `TrainingJobRegistry`, `TrainingJobSnapshot`.
- `TrainingEndpoints`: POST/GET/DELETE/promote, with server-side report re-read, recomputation,
  policy+verifier re-run, ledger verification, stale-champion guard.
- `RsiJobValidator`, `RsiJob`, `RsiJobRegistry`, `RsiEndpoints` with the same promote discipline for
  proposer artifacts; a meta promotion never touches `active-champion.json`.

### Viewer (`wwwroot/viewer`)

- Fifth **Training** tab, form, live timeline, promote button behind `window.confirm`, champion card,
  same-origin assets, textContent-only rendering. (Implemented by a delegated UI task; verified by
  `TrainingUiAssetTests` and a manual browser run.)

### Tests (`WIP/Wip.Tholdem.Tests`)

- `Training/TrainingJobApiTests.cs` (10), `Training/TrainingPromoteApiTests.cs` (5),
  `Training/RsiJobApiTests.cs` (3), `Training/ThreatControlTests.cs` (9),
  `Training/TrainingUiAssetTests.cs` (3), `Learning/MetaLoopTests.cs` (12).

## Verified so far

- Pre-change baseline: `TOTAL 275 PASSED 275 FAILED 0 SKIPPED 0`.
- After the Learning/Web changes, before the new tests: the same `TOTAL 275 PASSED 275 FAILED 0`.
- Real loopback host on `127.0.0.1:5100`:
  - `POST /api/training` with a 0.70/0.70 starting artifact, 1 iteration, 50 evaluation seeds,
    10 evaluation hands, margin 0, min seeds 2 returned
    `jobId train-dbadcdd70b0c15a2`;
  - the job completed with `meanBbPer100 1.05`, `lowerBoundBbPer100 0.3213406234713775`,
    `decision promoted`, `outcome promoted`, `canPromote true`;
  - `POST .../promote {}` returned `409 confirmation-required`;
  - `POST .../promote {"confirm":true}` returned `200 already-promoted`, champion
    `tholdem-strategy-v2-f7a0e101`, report hash
    `7a44736a282b8c8322c0ef22b8faac07a057de6b6bd79664bb24492742d36a13`.
- Default-champion runs with 24–50 evaluation seeds reported exactly `mean 0`, `LCB 0`,
  `lcb-below-margin`: the shipped proposer's nudges are behaviourally inert against the baseline.
  This is recorded as a limitation, not hidden; the promotion demo uses an explicit starting artifact.

## Final verification

- Full suite after mutation restore: `TOTAL 317 PASSED 317 FAILED 0 SKIPPED 0`; waiver expected total 317.
- Solution build: `0 Error(s)` (125 pre-existing warnings).
- Architecture: `TOTAL 4 PASSED 4 FAILED 0`; coverage `48/48` (`solution_coverage=ok`); `HarnessSync --check` exit 0.
- Mutation evidence: 11/11 rows red with the defense removed, every file restored by SHA-256 (`True`).
- Determinism: same options on two hosts → same job id/options hash and identical SHA-256 for all seven artifacts.
- UI: screenshots `training-tab.png`, `training-running.png`, `training-complete.png`, `training-promoted.png`, `training-390.png`; browser console errors 0.
- Ledger audit: 3 chained entries, verified head `45fe1998…`; champion v2 with report hash `7a44736a…`.

## Deliberate changes to existing expectations

None. The two pre-existing viewer tests that initially failed were fixed in the UI (kept the `1–4`
hint text and removed a fixed `42rem` training-table width); their assertions and names are unchanged.
