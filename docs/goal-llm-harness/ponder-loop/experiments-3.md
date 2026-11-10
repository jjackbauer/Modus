# Search-augmented Ponder+Enhance loop — a verified promotion

> Direct instruction: "do it" after the second series. Pre-registration: [preregistration-c.md](preregistration-c.md). The shape: a live 6-seat Ponder match every round, the live refiner proposes a **seed** genome from the champion's own 6-seat measurement, a deterministic **grid search** refines it on a seed set **disjoint** from the gate, and the frozen gate on **untouched** seeds decides. Three rounds at seed `1099512627776`.

## Result: one verified promotion

| Round | LLM seed (raise/call/bluff/sizing) | Search winner | Search LCB @50 | Gate mean | Gate LCB | Verdict | Champion after |
|---|---|---|---:|---:|---:|---|---|
| 1 | 0.60 / 0.55 / 0.18 / 1.25 | **0.90 / 0.20 / 0.18 / 1.00** | **43.362** | **33.594** | **11.885** | **promoted** | **v2-6a108c2b** |
| 2 | 0.78 / 0.30 / 0.26 / 1.35 | 0.90 / 0.20 / 0.26 / 1.00 | 0.274 | 0.833 | −3.065 | refused | v2-6a108c2b |
| 3 | 0.72 / 0.34 / 0.26 / 1.35 | 0.70 / 0.20 / 0.10 / 1.35 | 7.369 | 6.031 | −0.363 | refused | v2-6a108c2b |

**Final champion: `tholdem-strategy-v2-6a108c2b` — 0.90 / 0.20 / 0.18 / 1.0.** The pointer records the promoted iteration and its report hash:

```
{"strategyId":"tholdem-strategy-v2-6a108c2b","iteration":1,
 "evaluationReportHash":"139fd8331271dbfa5484e118aef201c5ae650d6625bd6c3aba888236ae826299"}
```

The promotion evaluation: 24 seeds × 20 hands, mean **33.59375** bb/100, lower bound **11.8854** > 5, `StatisticalPromotionPolicy` = promoted, `PromotionVerifier` = verified.

### Robustness on fresh seeds

The promoted genome was re-evaluated against the old 0.70/0.70 champion on two **fresh base seeds**, outside the gate's sample:

| Base seed | 24 seeds | 50 seeds | 100 seeds |
|---|---:|---:|---:|
| `1099512727776` | LCB 34.040 | LCB 27.732 | LCB 36.230 |
| `1099512827776` | LCB 29.682 | LCB 34.423 | LCB 33.583 |

All six evaluations promoted and verified. The strategy is not a one-seed artefact.

## What the hybrid fixed

- The model's round-1 seed was the familiar **loosening** genome (0.60/0.55/0.18/1.25). The grid search explored raise ∈ {seed, 0.70, 0.80, 0.90, 0.95}, call ∈ {seed, 0.20, 0.30, 0.40, 0.50}, bluff ∈ {seed, 0.10} and sizing ∈ {seed, 1.0}, evaluated **100 candidates** at 50 seeds on a **separate** base seed, and found **0.90/0.20/0.18/1.0** — the raise-up/call-down direction the model never proposed. The frozen gate then promoted it on untouched seeds.
- Rounds 2–3 show the gate still refusing: the search can find high-LCB challengers relative to the *old* champion, but beating the new 0.90/0.20 champion by 5 bb/100 lower bound is harder. That is the frozen gate working, not a failure.
- No selection on the evaluation sample: search seeds (`Derive(seed, 7000+round)`) and gate seeds (`Derive(seed, 9000+round)`) are disjoint.

## Each round also pondered (all live, 6 seats)

| Round | Model turns | Tool calls | Tools | Legal actions | Errors | Seat-0 6-seat sample |
|---|---:|---:|---|---:|---:|---|
| 1 | 74 | **34** | equity-calculator, pot-odds | 45 | 0 | — |
| 2 | 85 | **43** | equity-calculator, pot-odds | 51 | 0 | — |
| 3 | 61 | **28** | equity-calculator, pot-odds | 39 | 0 | — |

The Ponder surface ran every round; the enhance used the champion's deterministic 6-seat measurement (analysis source `genome`). The LLM seeds were repaired once each (the first response was malformed) and are recorded in the refiner transcripts.

## Files

- Full report: [strategies-report-c.md](strategies-report-c.md), [strategies-report-c.json](strategies-report-c.json).
- Search records: [search/round-1.json](search/round-1.json), `round-2.json`, `round-3.json` (100/80/100 candidates).
- Gate evaluations: [evaluations-c/](evaluations-c/) (three canonical 24-seed reports).
- Champion: [champion-c.json](champion-c.json) (pointer), [strategies-c/](strategies-c/) (promoted artifact).
- Refiner transcripts: [refiner/round-c-1.jsonl](refiner/round-c-1.jsonl) etc.
- Run log: [live-search.log](live-search.log).
- Hashes: [artifacts.sha256](artifacts.sha256).

*No API key in any artifact; the gate, the frozen components and the deterministic mode were not modified. The promotion is in the loop's scratch data directory (`.tmp/goal-llm-harness/loop/live-search/refiner-data/`), not in the product's champion pointer.*
