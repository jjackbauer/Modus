# Pre-registration C — search-augmented Ponder+Enhance loop

> Direct instruction: "do it" — run the hybrid experiment identified after the second series: the LLM proposes a seed, a deterministic search refines it on a **separate** seed set, and the frozen gate on untouched seeds decides.

## Shape

| Item | Value |
|---|---|
| Rounds | **3** |
| Seats | **6** (live Ponder match every round; the genome measurement is deterministic) |
| Hands per round | 20 |
| Run seed | `1099512627776` (verified unused) |
| Round Ponder seed | `SeedDerivation.Derive(runSeed, round, 0, 0, 0)` |
| Refiner analysis | genome-6seat (the champion's own measurement, as in batch B) |
| LLM seed | live `LlmRefinementPolicy`, one proposal per round, one repair allowed |
| Search | grid on {seed.raise, 0.70, 0.80, 0.90, 0.95} × {seed.call, 0.20, 0.30, 0.40, 0.50} × {0.10, seed.bluff} × {1.0, seed.sizing}, clamped, evaluated at **50 seeds × 20 hands** on base seed `Derive(runSeed, 7000 + round, ...)` (disjoint from the gate) |
| Search winner | highest LCB; tie-break higher mean |
| Gate | the search winner evaluated at **24 seeds × 20 hands** on base seed `Derive(runSeed, 9000 + round, ...)`, default criteria, frozen policy/verifier |
| Promotion | a verified promotion updates the champion; the next round's measurement/search uses it |

## Hypotheses

1. The grid contains the clearing region found earlier (raise 0.90–0.95, call 0.20–0.30, bluff 0.10, sizing 1.0), so the search will find a candidate the LLM alone never proposed.
2. Evaluated on a disjoint gate seed set, that candidate will promote (the earlier 24/50/100-seed check gives prior confidence).
3. With the champion updated, later rounds test whether further search can beat the new champion.

## Invariants

- Search seeds and gate seeds are disjoint (no selection on the evaluation sample).
- The gate, its components and the deterministic mode are untouched; no test/gate change.
- No key in any artifact; a failure is recorded, not fabricated.
