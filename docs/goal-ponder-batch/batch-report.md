# Aggressive Ponder+Enhance batch — 10 runs, 12 promotions

> Request: "do it more agressively now and more times. at least 10 times this level of experiment run." Pre-registration: [preregistration.md](preregistration.md). Ten independent runs, each 4 live 6-seat rounds (20 hands), an LLM seed, a 378-capacity grid search on disjoint 50-seed data, and the frozen gate on untouched 24 seeds. Seeds `1099513000001`–`1099513000010`.

## Per-run results

| Run | Seed | Promotions | Gate LCBs (rounds 1–4) | Final champion | Genome (raise/call/bluff/sizing) | Tool calls | Errors |
|---|---|---:|---|---|---|---:|---:|
| 1 | 1099513000001 | **2** | 20.46, 10.98, −15.73, −17.82 | v3-b4dd8c21 | 0.62/0.15/0.10/1.35 | 156 | 0 |
| 2 | 1099513000002 | 1 | 29.29, −26.95, −23.81, −9.52 | v2-ce0a90be | 0.70/0.15/0.18/1.10 | 118 | 0 |
| 3 | 1099513000003 | 1 | 28.39, −1.84, −3.12, −3.56 | v2-af92648d | 0.90/0.15/0.14/1.10 | 153 | 0 |
| 4 | 1099513000004 | 1 | 3.43, 20.72, 4.87, 2.05 | v2-c2ac7636 | 1.00/0.15/0.14/1.00 | 137 | 0 |
| 5 | 1099513000005 | 1 | 30.55, −8.76, −44.06, −53.69 | v2-ce0a90be | 0.70/0.15/0.18/1.10 | 151 | 0 |
| 6 | 1099513000006 | 1 | 24.85, −27.28, 0.32, 3.41 | v2-25a644e8 | 0.95/0.15/0.14/1.00 | 124 | 0 |
| 7 | 1099513000007 | 1 | 18.83, −4.90, −0.88, −40.92 | v2-cc96dffb | 0.80/0.15/0.18/1.10 | 165 | 0 |
| 8 | 1099513000008 | 1 | **6.02**, −44.58, −8.41, −7.02 | v2-7e4e9a5f | 0.85/0.15/0.18/1.10 | 144 | 0 |
| 9 | 1099513000009 | **2** | 27.45, **6.55**, 0.00, −6.45 | v3-2c9559bc | 0.70/0.20/0.14/1.00 | 147 | 0 |
| 10 | 1099513000010 | 1 | 36.77, −10.21, 3.44, 0.21 | v2-45a371d1 | 1.00/0.15/0.18/1.00 | 177 | 0 |

**Aggregate:** 10/10 runs completed, 4/4 rounds each, 6 seats, live `deepseek-flash`, **0 errors**, **1,472 tool calls**, **12 verified promotions**, **10 distinct promoted genomes**. Every run promoted at least once; only runs 1 and 9 promoted twice. **All 12 promotions happened in rounds 1–2**; no run improved its champion after round 2.

## The ten distinct promoted strategies

| Genome (raise/call/bluff/sizing) | Promoted in | Gate LCB(s) | Fresh-seed checks | Robust? | Fresh LCB range |
|---|---|---:|---:|---|---:|
| 0.62/0.15/0.10/1.35 | run 1 r2 | 10.98 | 2/4 | no | −41.8…25.4 |
| 0.70/0.15/0.18/1.10 | runs 2 r1, 5 r1 | 29.29 / 30.55 | 3/4 | no | −6.8…43.0 |
| 0.70/0.20/0.14/1.00 | run 9 r2 | 6.55 | 1/4 | no | −12.4…30.8 |
| 0.80/0.15/0.18/1.10 | run 7 r1 | 18.83 | 4/4 | **yes** | 8.3…33.6 |
| 0.85/0.15/0.18/1.10 | run 8 r1 | 6.02 | 4/4 | **yes** | 13.7…37.1 |
| 0.90/0.15/0.14/1.10 | run 3 r1 | 28.39 | 4/4 | **yes** | 24.7…29.3 |
| 0.95/0.15/0.14/1.00 | run 6 r1 | 24.85 | 4/4 | **yes** | 24.9…27.9 |
| 1.00/0.15/0.14/0.85 | run 9 r1 | 27.45 | 4/4 | **yes** | 25.0…28.0 |
| 1.00/0.15/0.14/1.00 | run 4 r2 | 20.72 | 4/4 | **yes** | 25.0…28.0 |
| 1.00/0.15/0.18/1.00 | runs 1 r1, 10 r1 | 20.46 / 36.77 | 4/4 | **yes** | 28.9…32.6 |

**Fresh-seed robustness: 34/40 checks; 7/10 genomes pass all four checks.** The three failures are gate-sample artefacts, including the 6.55 promotion; the 6.02 promotion survives. The most robust region is raise **0.80–1.00**, call **0.15–0.20**, bluff **0.14–0.18**, sizing **0.85–1.35**.

## What the batch establishes

1. **The hybrid is reliable at producing a promotion.** 10/10 runs promoted; 9/10 first promoted in round 1. The weak start champion (0.70/0.70) is reliably beaten by the search+gate shape.
2. **Gains saturate immediately.** Only 3/12 promotions came after round 1; none after round 2. The new champions are much harder to beat, and later search winners often fail the gate (e.g., run 1 r3 search LCB 47.12 → gate −15.73).
3. **The gate's margin is real at the boundary.** Refusals at 3.43, 4.87, 2.05, 3.41, 3.44; promotions at 6.02/6.55. The 6.55 one is not fresh-robust; the 6.02 one is.
4. **The search has a selection bias.** A wider grid produces higher *search-sample* LCBs; the disjoint gate catches the overfit. The search LCB is not a promotion predictor by itself.
5. **The winning direction is consistent:** call 0.15 (9/10), raise 0.80–1.00 (7/10), bluff 0.14–0.18, sizing 1.0–1.35 — the opposite of the LLM's original loosening bias.

## Search accounting

The aggressive grid capacity is 378 (7×6×3×3). The **evaluated** count per round is usually 378, but dedup lowers it when the live seed genome already sits on grid anchors: observed 180, 252, 315, 324 and 378. The per-round `search.json` records the exact count and `aggressive=true`; no round silently truncated the search.

## Evidence and artifacts

- Per-run reports: `runs/run-01.json` … `runs/run-10.json` (copied from the live run dirs); full live data under `.tmp/goal-llm-harness/batch/run-01..10/`.
- Batch log: [logs/batch-runs.log](logs/batch-runs.log) (10 exit-0, completed-4 lines).
- Pointers: `pointers/run-XX.json`; each promotion's canonical evaluation is in the run's `evaluations/`.
- Fresh-seed check: `.tmp/goal-llm-harness/batch-confirm/`; results in [evidence.md](evidence.md) EVD-021/022.
- Evidence: EVD-016..022; journal OBS-010/011, VAL-008.
- No API key in any artifact; the gate, frozen components and deterministic mode were untouched; the product champion pointer was not changed.

*The batch is an exploratory multi-run demonstration, not a claim that every promotion generalizes. The 3/10 fresh-seed failures are reported above, not averaged away.*
