# Goal — Tholdem Continual Harness Refinement

- **One-sentence outcome**: The Tholdem learning loop gains an opt-in, data-only harness-refinement path that turns recorded hands into deterministic failure signatures, persistent memory, lineage, bootstrap state and deterministic skill edits, leaves the frozen statistical gate and legacy mode byte-identical, and in a pre-registered multi-seed experiment measurably increases the behavioural exploration of proposals without weakening promotion evidence.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| D1 | Legacy equivalence | Build `WIP/Wip.Tholdem.Agents` in Release; run `train --seed 1099511627776 --iterations 2` into two fresh directories with refinement off | Every JSON artifact hash equals the recorded pre-change baseline in `round-0/baseline-hashes.txt` (7/7 files, both directories) |
| D2 | Trajectory capture and signatures | Run the focused refinement tests and a probe over one real training match | Captured hand count equals `TrainingHands`; scratch directory is removed; signature JSON is canonical, has at least four named signals and contains no seed, card or opponent name |
| D3 | Memory and lineage | Run the focused memory/lineage tests | Third rejection marks a dead end; 65th entry evicts deterministically; tampered memory is refused; missing parent and cycle are refused with stable codes |
| D4 | Refinement policy and bootstrap | Run the focused policy/bootstrap tests | A signature produces the implicated parameter edit; an empty context falls back to the legacy nudge; frozen bootstrap leaves memory bytes unchanged; updating bootstrap changes them; two fresh directories produce identical artifact hashes |
| D5 | Guards and safety | Run the focused guard/safety tests, the existing always-promote control and the extended source scan | Inert skill edit refused before evaluation; gate-target edit rejected; memory-only edit writes no report; nine edits refused; always-promote still fails; no forbidden token and no model/network call |
| D6 | Deterministic skills | Run the skill tests over the recorded seeded corpus | Default skills reproduce today's actions and hand hashes byte-for-byte; a preflop parameter changes only preflop decisions; an illegal skill action falls back legally; an absent-skills artifact keeps its old hash |
| D7 | Ablation runner | Run the ablation tests and one real six-configuration ablation | Canonical `tholdem.ablation.v1` report; repeat run byte-identical; `active-champion.json` and `meta/active-proposer.json` untouched; budget overflow refused |
| D8 | Refiner-policy evolution | Run the meta-loop refinement tests | Default genome behaves identically to today; frozen manifest and gate unchanged; a candidate that wins meta seeds and loses fresh seeds is refused |
| D9 | Significant experiment | Run the pre-registered paired experiment on dev seeds and then once on holdout seeds | On holdout, median decision-divergence ratio (refinement / baseline) >= 1.5 with a paired 95% bootstrap CI whose lower bound is > 1.0; zero-effect evaluation rate is not higher; no gate weakening; raw JSON and a report with the calculation are stored in the goal directory |

## Experiment pre-registration

- **Arms**: baseline = `HarnessRefinement=false`; refinement = `HarnessRefinement=true`; both start from a fresh data directory and the default champion.
- **Iterations**: 5 per seed; training hands 120; evaluation seeds 24; evaluation hands 10.
- **Dev seeds** (tuning only): `1099511627776 + i * 1000` for `i = 0..5`.
- **Holdout seeds** (used once for the final claim): `1099511727776 + i * 1000` for `i = 0..5`.
- **Primary metric**: decision divergence per iteration = fraction of 120 probe hands whose canonical action-log hash differs between the champion and the challenger on the same probe seed. The probe seed is the iteration's evaluation base seed (`SeedDerivation.Derive(jobSeed, 5000 + iteration, 0, 0, 3)`), so the comparison uses held-out deals.
- **Secondary metrics**: zero-effect evaluation rate (`|meanBbPer100| < 0.05` and `|lowerBoundBbPer100| < 0.05`), verified promotions, mean bb/100.
- **Success rule**: holdout median divergence ratio >= 1.5, paired 95% bootstrap CI lower bound > 1.0, and refinement zero-effect rate <= baseline zero-effect rate. A failure of the rule is reported as an honest null, not rounded up.
- **No gate reads the experiment**: the promotion gate and verifier are unchanged; the experiment reads artifacts and writes only goal/scratch files.

## Hard constraints

- Modify only `WIP/Wip.Tholdem.Agents/Learning/**`, `WIP/Wip.Tholdem.Tests/**`, `docs/tholdem/**`, `docs/goal-tholdem-harness/**`, and scratch under `.tmp/goal-refinement/**` (plus an isolated `.tmp` probe project if needed).
- Do not modify the engine, history format, `MatchSimulator` public contract, `MatchEvaluator`, `EvaluationCriteria`, `PromotionPolicy`, `PromotionVerifier`, `SeedDerivation`, `Wip.Bones*`, `Wip.Shell*`, `Wip.Runtime`, or `Wip.slnx`; add no project to the solution.
- Legacy mode must remain byte-identical, including every recorded artifact hash.
- No existing test may be weakened, skipped, deleted or have its expectations changed; every changed expectation must be justified in the round artifact.
- No model call, network socket, process start, dynamic compile or assembly load on the refinement path. No clock, GUID or culture-dependent value in persisted artifacts.
- Offline only; no publish, merge, tag or deploy; no CI-green claim.

## Stop criteria

- D1–D9 are `Covered` at High confidence, zero `Falsified` rows remain, and zero P0 open questions remain.
- The round cap is **10**. The same blocking condition for three consecutive rounds is the only reason to mark the goal blocked, with that condition named concretely.
