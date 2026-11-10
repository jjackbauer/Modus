# Product promotion — the batch winners against the real champion

> Direct instruction: "promote them". The product champion was **not** the batch's 0.70/0.70 scratch root: `artifacts/tholdem` held **`tholdem-strategy-v1-f100e793` = 0.72/0.45/0.12/1.0** (iteration 0, legacy 8-seed ledger). Every candidate was therefore re-gated against that champion, not against the batch root.

## Dry gate vs the product champion

A fresh 24×20 gate (base `1099515000001`) of the seven fresh-robust batch genomes against 0.72/0.45:

| Genome | Mean | LCB | Verdict |
|---|---:|---:|---|
| 0.80/0.15/0.18/1.10 | 32.594 | −3.197 | refused |
| 0.85/0.15/0.18/1.10 | −4.021 | −35.955 | refused |
| 0.90/0.15/0.14/1.10 | 32.938 | 6.788 | pass |
| 0.95/0.15/0.14/1.00 | 13.542 | 6.348 | pass |
| 1.00/0.15/0.14/0.85 | 8.698 | 2.955 | refused |
| 1.00/0.15/0.14/1.00 | 15.938 | 11.281 | pass |
| 1.00/0.15/0.18/1.00 | 23.802 | 14.545 | pass |

## The actual chain (fresh gate seeds, base `1099515000002`)

Candidates were evaluated in ascending strength; each promoted champion became the incumbent for the next candidate, so a candidate that passed the dry gate still had to beat the *new* champion.

| Iter | Candidate | Artifact | Mean | LCB | Verdict |
|---:|---|---|---:|---:|---|
| 4 | 0.80/0.15/0.18/1.10 | v2-c1c73b5a | 18.521 | −14.187 | refused |
| 5 | 0.85/0.15/0.18/1.10 | v2-0ff007aa | 43.260 | −2.705 | refused |
| **6** | **0.90/0.15/0.14/1.10** | **v2-562c9cf5** | **17.417** | **10.832** | **promoted** |
| 7 | 0.95/0.15/0.14/1.00 | v3-a60c961c | −0.146 | −3.628 | refused |
| 8 | 1.00/0.15/0.14/0.85 | v3-0550218b | −1.094 | −4.567 | refused |
| 9 | 1.00/0.15/0.14/1.00 | v3-1c469637 | −3.260 | −6.254 | refused |
| 10 | 1.00/0.15/0.18/1.00 | v3-06d3c9a1 | 1.812 | −4.093 | refused |

**Final product champion: `tholdem-strategy-v2-562c9cf5` — 0.90 / 0.15 / 0.14 / 1.10**, iteration 6, parent `v1-f100e793`.

## Verification

- `TrainingLedger.Verify("artifacts/tholdem")`: **present, verified, 10 entries, 10 chained, 0 legacy, head `e974e34e01fb…`** — the legacy 3-entry ledger was upgraded to the SHA-256 chain on write.
- Pointer: `{"strategyId":"tholdem-strategy-v2-562c9cf5","iteration":6,"evaluationReportHash":"1e8908dbfafba4231afdc2144e7548728a2c4c60be2e92c2ba5115a31db47485"}`.
- Artifact: version 2, parent `v1-f100e793`, `createdAtIteration=6`, genome 0.90/0.15/0.14/1.10.
- One promoted ledger entry out of 10; the six refusals are recorded, not hidden.
- Backup of the pre-promotion product data: `.tmp/goal-llm-harness/product-backup-20261110-061544/`.
- `artifacts/` is gitignored (local product state); this report is the tracked record of the promotion.

*The chain promoted one of the seven; the 0.95/1.00 variants that passed the dry gate did not beat the new 0.90 champion. That is the frozen gate deciding, not a selection.*
