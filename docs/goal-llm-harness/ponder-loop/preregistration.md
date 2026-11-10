# Pre-registration — live 6-seat Ponder + Enhance loop

> Direct human instruction (2026-11-09): **"do a reasonable number of round of ponder and enhance and expose to me the arising strategies. use the max number of seats available. all pondering"**. This is an exploratory live loop, not a gated promotion claim. Every round is a real Ponder match; every enhance is a real refiner call; the frozen gate evaluates each arising strategy.

## Shape (frozen before the run)

| Item | Value |
|---|---|
| Rounds | **4** |
| Seats | **6** (engine maximum; the LLM agent at seat 0 + five `TightAggressiveAgent` baselines) |
| Hands per round | 20 |
| Run seed | `1099512227776` (verified unused: 0 hits in `docs`, 0 in `.tmp`) |
| Round deal seed | `SeedDerivation.Derive(runSeed, round, 0, 0, 0)` |
| Gate evaluation | 24 seeds × 20 hands, heads-up, default `EvaluationCriteria` (margin 5, minSeeds 24) |
| Evaluation base seed | `SeedDerivation.Derive(runSeed, 9000 + round, 0, 0, 0)` |
| Ponder agent | live `TholdemRuntimeAgent`, `both-required` tool prompt, `deepseek-chat`, four fixed tools |
| Refiner | live `LlmRefinementPolicy`, `deepseek-chat`, 512 max tokens, 0.2 temperature, one repair, 4 calls/round |
| Champion start | `0.70/0.70/0.12/1.0` version 1, iteration 0 |

## Each round

1. **Ponder** — 6-seat, 20-hand live match; record every model call, tool call, tool result, action, latency and error; write the session and transcripts.
2. **Enhance** — build a real `RefinementContext` from that round: the incumbent champion genome, `SessionAnalysis` from the seat-0 metrics of the 6-seat report, `SessionSignatures` extracted from the round's `HandRecord`s, and the round index. The live refiner proposes one genome; its transcript is recorded.
3. **Gate** — the frozen `MatchEvaluator`, `StatisticalPromotionPolicy` and `PromotionVerifier` evaluate the proposal heads-up against the champion at 24 seeds × 20 hands. A verified promotion updates the champion; otherwise the proposal is recorded as refused.

## Exposure

A per-round report with: the Ponder metrics (turns, tool calls, legal actions, errors, seat-0 win rate/bb-100/aggression/showdown), the incumbent and candidate genomes, the refiner's rationale/reason, the gate mean/LCB/verdict, the promoted flag, and the resulting champion. The final report lists every arising strategy and the final champion.

## Fidelity notes (stated, not hidden)

- The Ponder agent's decisions come from the **LLM**, not from the `StrategyGenome`; the genome is the object the refiner proposes and the gate evaluates. The loop couples them through the round's measured analysis and signatures, not by feeding the genome into the Ponder prompt.
- The gate evaluation is heads-up because the frozen components are heads-up; the Ponder matches are 6-seat because the user asked for the engine maximum. The mismatch is deliberate and reported.
- A model refusal or transport failure is recorded and stops the round; no fallback candidate is fabricated.
- No release/publish/CI claim; no key in any artifact.
