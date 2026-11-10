# Ponder + Enhance — second experiment series

> Continuation after the first four live rounds. The question was why the refiner's strategies were all refused. Four further experiments: (B) a corrected loop that feeds the refiner the **genome's own** measurement plus gate-refusal memory; (C) a **gate-aware** refiner shown the gate objective and the prior LCBs; (D) **best-of-10** proposals at 50 seeds; (E) a deterministic **gate search** as the benchmark. All live calls were real; all gate evaluations used the frozen `MatchEvaluator` + `StatisticalPromotionPolicy` + `PromotionVerifier`.

## 1. Six-seat scoring of the first batch

The first batch's strategies were scored in the same 6-seat field they were derived from (seat 0 = genome as `StrategyAgent`, seats 1–5 tight-aggressive, 24 seeds × 20 hands, same seeds for every genome):

| Genome | 6-seat mean bb/100 | Paired diff vs champion | Paired LCB |
|---|---:|---:|---:|
| champion 0.70/0.70/0.12/1.00 | **+140.9** | — | — |
| A1 0.55/0.60/0.18/1.25 | −11.7 | −152.6 | −486.3 |
| A2 0.60/0.55/0.18/1.25 | −24.9 | −165.8 | −387.9 |
| A3 0.60/0.62/0.18/1.25 | +62.3 | −78.7 | −278.4 |
| A4 0.62/0.66/0.18/1.15 | +89.8 | −51.1 | −187.8 |

The champion beats every first-batch candidate in the 6-seat field too. The refiner's loosening was wrong in both environments — not a heads-up/6-seat mismatch.

## 2. Corrected loop (batch B): genome measurement + gate memory

Each round: the live 6-seat Ponder match still ran, but the refiner's analysis/signatures came from a separate **genome** 6-seat match (`StrategyAgent(champion)` at seat 0), and every gate verdict was written to `HarnessMemoryStore` (refusal → failed-approach, promotion → pattern). Seed `1099512427776`, 4 rounds, 6 seats.

| Round | Candidate | raise/call/bluff/sizing | Gate mean | Gate LCB | Verdict |
|---|---|---|---:|---:|---|
| 1 | `v2-865fb017` | 0.55/0.62/0.18/1.15 | −1.135 | −88.741 | refused |
| 2 | `v2-feffefe3` | 0.60/0.55/0.10/1.25 | −13.677 | −60.659 | refused |
| 3 | `v2-7d1c8d53` | 0.60/0.62/0.18/1.25 | −18.396 | −59.148 | refused |
| 4 | `v2-62d307d7` | 0.62/0.66/0.18/1.15 | +44.771 | −17.303 | refused |

**Still no direction change.** Round 4's reason is diagnostic: *"Prior tightening attempts failed; nudge thresholds down..."* — but the memory contained the refiner's own **loosening** refusals, which it misread as failed tightening. The 20-hand genome sample was also noisy: the champion's 6-seat sample showed −42.5 bb/100 in round 1, while its 24-seed mean is **+140.9**. The refiner was reacting to noise.

6-seat scoring of batch B: champion +140.9; B1 +99.2; B2 −34.6; B3 +62.3; **B4 +165.8** (paired +24.8 vs champion, paired LCB −205.4; not a reliable improvement).

## 3. Gate-aware refiner (3 proposals)

The model was shown the gate rule (LCB > 5 over 24 seeds), the champion's robust 6-seat result, and all eight prior genomes with their gate means/LCBs. All three responses had a `reason` over the 280-character contract, so the parser refused them; the genomes were salvaged for scoring:

| Try | Genome | Gate mean | Gate LCB | Verdict |
|---|---|---:|---:|---|
| 1 | 0.62/0.66/0.18/1.15 | 34.073 | **3.922** | refused |
| 2 | 0.62/0.66/0.18/1.15 | 5.833 | −20.497 | refused |
| 3 | 0.68/0.70/0.13/1.00 | 1.719 | 0.321 | refused |

The same genome scored LCB 3.92 and −20.50 on different 24-seed base seeds — the gate's 24×20 sample is noisy.

## 4. Best-of-10 at 50 seeds

Ten gate-aware calls (temperatures 0.2–1.1, one repair allowed). The model re-proposed `0.62/0.66/0.18/1.15` in **7 of 10** tries, explicitly citing its prior +44.8 mean, and produced one near-incumbent and one intermediate genome. Best LCB: **2.568** (`0.66/0.68/0.14/1.05`); none cleared the gate.

## 5. Deterministic gate search — the benchmark

50 seeds × 20 hands, seed base `1099512527776`/9000, frozen criteria:

| Genome | Mean | LCB | Verdict |
|---|---:|---:|---|
| **0.95 / 0.20 / 0.10 / 1.00** | **28.775** | **23.931** | **promoted** |
| 0.95/0.20/0.10/1.00 @ 24 seeds | 27.760 | 21.164 | promoted |
| 0.95/0.20/0.10/1.00 @ 100 seeds | 23.738 | 15.614 | promoted |
| **0.90 / 0.30 / 0.10 / 1.00** | 28.775 | 13.077 | promoted |
| 0.90 / 0.25 / 0.10 / 1.00 | 28.775 | 13.077 | promoted |
| 0.80/0.40/0.10/1.00 (the old fixture) | 18.775 | −12.551 | refused |
| 0.80/0.45/0.12/1.00 | 27.700 | 3.011 | refused |
| 0.85/0.40/0.10/1.00 | 13.725 | −16.019 | refused |

Local search around the best LLM proposal (`0.66/0.68/0.14/1.05`) at the same bluff/sizing:

| Genome | Mean | LCB | Verdict |
|---|---:|---:|---|
| **0.90 / 0.30 / 0.14 / 1.05** | **34.825** | **19.057** | **promoted** |
| 0.90 / 0.40 / 0.14 / 1.05 | 22.935 | 8.264 | promoted |
| 0.90 / 0.50 / 0.14 / 1.05 | 17.005 | 6.870 | promoted |
| 0.84 / 0.50 / 0.14 / 1.05 | 16.995 | 0.541 | refused |

## Findings

1. **The LLM refiner never produced a gate-clearing genome** across 21 proposals (4 + 4 + 3 + 10). Its best LCB was **2.568**. It systematically **lowered** the raise threshold (0.55–0.68), while the clearing region **raises** it (0.90–0.95) and lowers the call threshold (0.20–0.30). The model's direction, not the plumbing, is the bottleneck.
2. **Prompt information did not fix it.** Explicit gate rule, prior LCBs, robust 6-seat incumbent, memory of refusals, best-of-10 — the model kept re-proposing its highest-mean loosening genome, conflating mean with lower bound.
3. **The gate is reachable and robust for the right genome.** `0.95/0.20/0.10/1.0` clears at 24, 50 and 100 seeds (LCB 21.16 / 23.93 / 15.61). A local search from the LLM's best proposal at raise 0.90 also clears (19.06 at 50). The old 0.80/0.40 fixture is seed-sensitive (cleared at 24 seeds on one base, −12.55 at 50 on another).
4. **The gate's 24×20 sample is noisy.** The same genome scored LCB 3.92 vs −20.50 on different 24-seed sets. A promotion claim from one 24-seed set is weak; the 0.95/0.20 genome is robust at 100 seeds.
5. **A search-augmented loop is the actionable design.** The LLM is useful as a proposal seed (the local search from its best proposal found a promoter at raise 0.90), but the winning move — raise the raise threshold — must be found by evaluation, not by the model's judgment. A hybrid (LLM proposes, local search refines, frozen gate decides) would have produced a promotion in this run.

## Artifacts

- Batch B reports: `.tmp/goal-llm-harness/loop/live-genome/strategies-report.{md,json}`; log `docs/goal-llm-harness/ponder-loop/live-genome.log`.
- Gate-aware probe: `.tmp/goal-llm-harness/gate-refiner/`; salvage eval `.tmp/goal-llm-harness/gate-salvage/`.
- Best-of-10: `.tmp/goal-llm-harness/best-of/`; transcripts in `refiner/gate-aware-best-of.jsonl` (copied).
- Gate search: `.tmp/goal-llm-harness/gate-search/`, `.tmp/goal-llm-harness/gate-local/`; results above.
- 6-seat scoring: `.tmp/goal-llm-harness/six-seat-eval/`.
- No key appears in any artifact; no gate/component was modified.
