# Goal — Tholdem promotion experiment with full refinement

- **One-sentence outcome**: Run a pre-registered promotion experiment from the documented suboptimal 0.70/0.70 starting champion under a legacy arm and a full refinement+skills arm with a deep budget, verify any promotion through the frozen gate, and report whether refinement changes the promotion outcome.

## Definition of done (falsifiable)

| # | Dimension | Check | Expected observable |
|---|---|---|---|
| P1 | Pre-run evaluation | Run the focused/full suite, inspect the frozen gate, validate the starting artifact and pointer, and run a baseline comparison of the start against the default v1 | 394/394 suite; five gate hashes unchanged; start artifact content-hash valid; start-vs-v1 report with mean/LCB recorded; `THOLDEM_LIVE_MODEL` unset and `DEEPSEEK_API_KEY` unset |
| P2 | Legacy arm | Run 10 iterations from the 0.70/0.70 start with refinement off | Raw per-iteration rows and ledger; final champion and pointer; any promotion verified |
| P3 | Refinement arm | Run the same seed/budget with `HarnessRefinement=true` and `SkillRefinement=true` | Raw per-iteration rows and ledger; final champion and pointer; any promotion verified; memory/lineage artifacts present |
| P4 | Promotion verification | For every promoted iteration, re-read the persisted report, re-run `StatisticalPromotionPolicy` and `PromotionVerifier`, and read the pointer | Report hash, seeds >= 24, LCB > 5, verifier agrees, pointer names the challenger |
| P5 | Comparison and conclusion | Compare legacy vs refinement: promotions, best LCB, best mean, zero-effect/inert count | A report that states whether refinement produced a promotion the legacy arm did not, or that no promotion occurred; no gate change |

## Pre-registration

- **Starting champion**: `StrategyGenome(RaiseThreshold: 0.70, CallThreshold: 0.70, BluffFrequency: 0.12, SizingMultiplier: 1.0)`, version 1, iteration 0, created in each fresh arm data directory; `active-champion.json` points at it before the run. This is the documented deliberate demo start, not a skill-discovery claim.
- **Seed**: `1099511927776` (fresh; not used by any prior experiment).
- **Criteria**: default `margin = 5.0 bb/100`, `minSeeds = 24`; the frozen gate is unchanged.
- **Budget (full deterministic "ponder")**: 10 iterations, 2000 training hands, 50 evaluation seeds, 20 hands per evaluation match. No LLM is called; the learning loop is deterministic. The live-model path is gated off because `THOLDEM_LIVE_MODEL` and `DEEPSEEK_API_KEY` are unset.
- **Arms**:
  - `legacy`: `HarnessRefinement=false`, `SkillRefinement=false`.
  - `refinement`: `HarnessRefinement=true`, `SkillRefinement=true`, `SignalRateFloor=0`, bootstrap `None`.
- **One attempt per arm**; no rerun for a favourable result. A technical failure may be fixed and the failed arm rerun, with every attempt reported.
- **Success**: at least one verified promotion in either arm. If none, report the best mean/LCB and the exact refusal reason; that is an honest no-promotion result.
- **No p-hacking**: the start, seed, budget, criteria and arms are fixed now. The holdout/gate are not changed; the experiment does not alter the shipped policy.

## Hard constraints

- Do not modify product code, tests, `Wip.slnx`, `.csproj` or the frozen gate. The experiment is a scratch probe plus goal documents.
- No network, model call, process start or dynamic compile; no live model.
- No release, publish, merge or deploy; no CI-green claim.
- The starting artifact is a deliberate demo start; a promotion or rejection is evidence about the gate/refinement, not a poker-strength claim.

## Stop criteria

- P1–P5 Covered at High confidence, zero Falsified delivery claims, zero P0 OQ; round cap **6**. A no-promotion result is a valid completion if the evidence is complete.
