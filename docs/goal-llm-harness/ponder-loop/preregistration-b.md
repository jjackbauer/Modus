# Pre-registration B — corrected loop (genome measurement + gate feedback)

> Follow-up experiment after the first loop's diagnosis. The first batch showed the refiner was diagnosing the **LLM's** Ponder play and proposing genomes that were worse than the champion in **both** the 6-seat field and the heads-up gate. This batch changes the refiner's input to a measurement of the **genome itself**, and feeds each gate verdict back through the harness memory.

## Shape (frozen before the run)

| Item | Value |
|---|---|
| Rounds | **4** |
| Seats | **6** (LLM Ponder match still runs every round; the genome measurement is a separate deterministic 6-seat match) |
| Hands per round | 20 |
| Run seed | `1099512427776` (verified unused: 0 hits in `docs`, 0 in `.tmp/goal-llm-harness`) |
| Round seed | `SeedDerivation.Derive(runSeed, round, 0, 0, 0)` |
| Refiner analysis source | **genome-6seat** — seat 0 = `StrategyAgent(champion)`, seats 1–5 = tight-aggressive, same 20 hands and round seed; its `SessionAnalysis` and `FailureSignatureExtractor` signatures feed the refiner |
| LLM Ponder | still runs every round (live `TholdemRuntimeAgent`, `both-required`, four tools), recorded; its analysis is **not** the refiner input |
| Memory feedback | after every round: refusal → `HarnessMemoryStore.RecordRejection(rationaleCode, round)`, promotion → `RecordPattern`; the next refiner prompt sees the entries |
| Gate | 24 seeds × 20 hands heads-up, default criteria; no gate change |

## Hypotheses

1. With the genome's own 6-seat measurement, the refiner will stop proposing the loosening edits it derived from the LLM's play.
2. With refusal feedback in memory, repeated failed rationale codes become dead ends and the next proposal changes direction.
3. A candidate may clear the heads-up gate (LCB > 5); if it does, the champion updates and the next round's genome measurement uses the new champion.

## Exposure

Per round: the LLM Ponder turns/tool calls; the genome 6-seat analysis and signals; the candidate genome, rationale and reason; the gate mean/LCB/verdict; the memory state; the champion. Final report at `docs/goal-llm-harness/ponder-loop/strategies-report-b.*`.

## Invariants

- The five frozen components, the deterministic mode and the existing results are untouched.
- No rerun for a favourable draw; a model/transport failure stops the round and is recorded.
- No key in any artifact.
