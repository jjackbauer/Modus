# Goal — aggressive Ponder+Enhance batch

## User request (verbatim)

> "okay, do it more agressively now and more times. at least 10 times this level of experiment run. /stretch-goal-harness"

## One-sentence outcome

**At least ten independent, pre-registered search-augmented Ponder+Enhance runs — each with four live 6-seat Ponder rounds, an LLM seed proposal, a wider deterministic grid search on a disjoint seed set, and a frozen-gate decision on untouched seeds — complete and are independently verified, with every run's strategies, promotions and refusals exposed in a batch report.**

## Named mechanisms (intent anchor)

| Mechanism | Status | Where it lives in this proposal |
|---|---|---|
| Search-augmented Ponder+Enhance ("this level of experiment run") | **KEPT** | Each run: live 6-seat Ponder, LLM seed, grid search, frozen gate |
| "at least 10 times" | **KEPT** | D1: 10 independent runs, fresh seeds |
| "more aggressively" | **KEPT** | D1: grid widened from 100 to 378 candidates; 50 search seeds; four rounds |
| "/stretch-goal-harness" | **KEPT** | This goal, journal, evidence, state, deliverables, gates, intent audit |

No named mechanism is replaced or removed. Nothing is a deterministic proxy for a mechanism the user named: every Ponder match is live and every enhance begins with a live model proposal.

## Definition of done

| ID | Check (falsifiable) | Command / observable |
|---|---|---|
| D1 Batch | ≥10 independent runs at distinct fresh seeds; each run 4 rounds × 6 seats × 20 hands, live `TholdemRuntimeAgent` with four tools, LLM seed, grid 378 candidates at 50 seeds on base `Derive(seed, 7000+round)`, gate 24 seeds on `Derive(seed, 9000+round)` | Batch log line per run; per-run `strategies-report.json` with `completedRounds=4`, `seats=6`, `analysisSource=genome`; `round-*/llm-transcripts.jsonl` have live served model |
| D2 Verified | For every run: report exists; the promoted champion (if any) re-derives from its canonical evaluation; gate LCB/mean match; search seeds disjoint from gate seeds | Open each report, pointer, evaluation; recompute mean/LCB/decision; check seed derivations |
| D3 Exposure | A batch report lists each run's seed, rounds, promotions, final champion, gate LCBs, Ponder turns/tool calls, and every arising strategy; aggregate promotion count | `docs/goal-ponder-batch/batch-report.md` + `.json`; every row cites a run path |
| D4 Invariants | Five frozen components unchanged; deterministic mode/tests unchanged; no key in any artifact; product champion pointer unchanged; no gate/component edit; no rerun to select a favourable draw | Hash the five; grep artifacts for key prefix; pointer diff; each run's seed pre-registered before it runs |
| D5 Honesty | Every refusal/failure recorded, no fabricated promotion, no omitted "could not verify" item | Journal/EVD rows per run; `OQ` for anything unverified |

## Hard constraints

- **Gate fixed**: margin 5 bb/100, minSeeds 24, frozen `MatchEvaluator`/`StatisticalPromotionPolicy`/`PromotionVerifier`.
- **Search/gate disjoint**: search seeds derive from `7000+round`, gate seeds from `9000+round`; no selection on the evaluation sample.
- **No product change**: the product champion pointer, the shipped deterministic mode and the five frozen components are untouched; promotions live in each run's scratch data dir.
- **No key exposure**: the secret is loaded into child processes only; artifacts are scanned.
- **Pre-registration**: the 10 run seeds are fixed before the batch starts; no seed substitution after seeing a result.
- **No cherry-picking**: all 10 runs reported, including failures.

## Stop criteria

1. At least 10 runs completed and verified (D1–D5), with the batch report written; or
2. A concrete blocker (e.g., model unavailable across retries) persists for 3 consecutive goal rounds — reported with the concrete condition, not "it is hard".

Round cap: **14**.

## Baseline (prior evidence, not a constraint)

The first search-augmented run (seed `1099512627776`) promoted `0.90/0.20/0.18/1.0` at gate LCB 11.8854. This batch tests whether that result recurs across independent runs and whether a wider search produces more/better promotions; the prior run is **not** counted toward the ten.
