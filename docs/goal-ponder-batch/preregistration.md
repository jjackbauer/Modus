# Pre-registration — aggressive batch (10 runs)

> Frozen before the first run. The ten seeds were verified unused (0 hits in `docs` and `.tmp/goal-llm-harness`) before this file was written. No seed is substituted after a result.

## Runs

| Run | Seed |
|---|---:|
| 01 | `1099513000001` |
| 02 | `1099513000002` |
| 03 | `1099513000003` |
| 04 | `1099513000004` |
| 05 | `1099513000005` |
| 06 | `1099513000006` |
| 07 | `1099513000007` |
| 08 | `1099513000008` |
| 09 | `1099513000009` |
| 10 | `1099513000010` |

Each run is one **search-augmented Ponder+Enhance** loop, four rounds (the more aggressive reading of DEC-003):

| Item | Value |
|---|---|
| Rounds | 4 |
| Seats | 6 (live `TholdemRuntimeAgent` + five `TightAggressiveAgent` baselines) |
| Hands per round | 20 |
| Champion start | `0.70/0.70/0.12/1.0` (each run independent) |
| Ponder | live, `both-required`, four tools, `deepseek-chat` |
| Refiner analysis | the champion genome's own 6-seat match |
| Search | **aggressive grid 378 candidates** (raise {seed,0.70,0.80,0.85,0.90,0.95,1.00} × call {seed,0.15,0.20,0.25,0.30,0.40} × bluff {seed,0.10,0.14} × sizing {seed,1.0,1.1} before dedup), 50 seeds × 20 hands on `Derive(seed, 7000+round)` |
| Gate | 24 seeds × 20 hands on `Derive(seed, 9000+round)`, default frozen criteria |
| Promotion | verified promotion updates that run's champion; later rounds search around it |

## Failure policy

- A run that exits non-zero or stops on a model/transport failure is recorded and **retried once with the same seed**; if it fails twice, the run is reported as failed and the batch continues. No replacement seed.
- All ten runs are reported, including refusals and failures; no cherry-picking.
- The gate, the five frozen components and the deterministic mode are untouched; the product champion pointer is not changed.

## Exposure

Per run: report, pointer, gate evaluations, search records, refiner/Ponder transcripts; aggregated in `docs/goal-ponder-batch/batch-report.md` and `.json` with a promotion tally and per-run strategies.
