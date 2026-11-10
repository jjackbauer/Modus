# Live 6-seat Ponder + Enhance loop — results

> Direct human request: "do a reasonable number of round of ponder and enhance and expose to me the arising strategies. use the max number of seats available. all pondering." Pre-registration: [preregistration.md](preregistration.md). Four live rounds, **6 seats** (engine maximum), 20 hands per round, seed `1099512227776`; each round is a real Ponder match followed by a real refiner call and a frozen-gate evaluation.

## The strategies that arose

| # | Candidate | raise / call / bluff / sizing | Refiner rationale | Gate mean | Gate LCB | Verdict | Promoted |
|---|---|---|---:|---:|---|---|---:|
| 1 | `tholdem-strategy-v2-068a6ead` | **0.55 / 0.60 / 0.18 / 1.25** | `llm-passive-play-loses` | 29.885 | **−40.494** | `lcb-below-margin` | no |
| 2 | `tholdem-strategy-v2-5f1a121b` | **0.60 / 0.55 / 0.18 / 1.25** | `llm-missed-value-showdown-loss` | −13.823 | **−45.935** | `lcb-below-margin` | no |
| 3 | `tholdem-strategy-v2-7d1c8d53` | **0.60 / 0.62 / 0.18 / 1.25** | `llm-missed-value-and-low-aggression` | −0.531 | **−34.508** | `lcb-below-margin` | no |
| 4 | `tholdem-strategy-v2-62d307d7` | **0.62 / 0.66 / 0.18 / 1.15** | `llm-proposal` | −36.677 | **−78.977** | `lcb-below-margin` | no |

**Final champion (unchanged):** `tholdem-strategy-v1-c9d1a24b`, `0.70 / 0.70 / 0.12 / 1.0`. The frozen gate refused all four proposals (LCB must exceed 5 over 24 seeds; none did). No promotion, so the champion pointer never moved.

### The model's own reasons (verbatim from the refiner transcripts)

1. *"Aggression factor is very low and missed value is frequent, so lower raise threshold and raise sizing slightly; tighten call threshold to reduce weak calls."*
2. *"Lower thresholds to raise/call more and reduce passivity, slightly increase bluffing and sizing to capture value missed at showdown."*
3. *"Lower raise threshold and raise sizing modestly to address missed value and low aggression; small bluff bump; slight call threshold reduction for balance."*
4. *"Low aggression and high fold-to-raise suggest more raising; high call-with-weak-hand and showdown losses suggest slightly tighter calls; small bump in bluff and sizing for value."*

## Each round: 6-seat Ponder (all live)

| Round | Seats | Hands | Ponder turns | Model calls | Tool calls | Legal actions | Errors | Seat-0 win rate | Seat-0 bb/100 | Aggression | Showdown |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 6 | 20 | 79 | 79 | **35** | 48 | 0 | 0.15 | −567.5 | 0.29 | 0.10 |
| 2 | 6 | 20 | 78 | 78 | **37** | 47 | 0 | 0.15 | −7.5 | 0.21 | 0.15 |
| 3 | 6 | 20 | 69 | 69 | **34** | 41 | 0 | 0.35 | +300.0 | 0.73 | 0.10 |
| 4 | 6 | 20 | 84 | 84 | **39** | 50 | 0 | 0.20 | +125.0 | 0.57 | 0.15 |

- 6 seats: the live `TholdemRuntimeAgent` at seat 0 plus five `TightAggressiveAgent` baselines; `both-required` tool prompt; four fixed tools.
- **145 tool calls** in total (equity-calculator and pot-odds), all with recorded results; **0 fallbacks, 0 violations, 0 errors**.
- Every call served `deepseek-flash`; per-round prompt tokens 154,833 / 155,680 / 137,437 / 167,258.
- Refiner calls: rounds 1–3 needed exactly one repair (first response malformed, second valid); round 4 was valid first time. Every refiner transcript records served model, usage and latency (1.14–1.41 s).

## What the loop shows

- **The arising strategies are consistent and systematically misaligned with the gate.** All four proposals *loosen* the incumbent (raise 0.55–0.62 vs 0.70; call 0.55–0.66; bluff up to 0.18; sizing 1.15–1.25). The refiner is reacting to a 6-seat session where seat 0 has low aggression and a low win rate — a different game from the heads-up gate. In the earlier heads-up fixture, *tightening* (0.80/0.40/0.10/1.0) was what cleared the gate. So the model's diagnosis ("play more") is coherent for the 6-seat table but not for the heads-up evaluation.
- **The gate held.** Four proposals with positive raw means in one case (29.9 bb/100) still failed the lower bound; none was allowed to move the champion.
- **No genome feedback into Ponder.** The Ponder agent's decisions come from the LLM, not from the genome; the genome is the refiner's proposal evaluated by the gate. The loop couples them through the round's analysis and signatures, not by feeding the genome back into the Ponder prompt. This is the honest limitation of the shape.

## Evidence

- Full report: [strategies-report.md](strategies-report.md), [strategies-report.json](strategies-report.json).
- Gate evaluations: [evaluations/](evaluations/) (4 canonical reports, 24 seeds × 20 hands each).
- Refiner transcripts: `refiner/round-{1..4}.jsonl` (real served model, usage, latency; rounds 1–3 include the malformed→repaired pair).
- Ponder sessions and model transcripts: `.tmp/goal-llm-harness/loop/live/round-{1..4}/` (cited in the JSON report; scratch, not copied).
- Run log: `docs/goal-llm-harness/ponder-loop/live.log`.

*No API key appears in any artifact. No release/publish/CI claim. The gate, the deterministic mode and the five frozen components were not modified.*
