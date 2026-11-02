# Goal Journal — bones-aggression-search

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---

### OBS-001: The v9 champion is the baseline to beat

- **Source**: `.tmp/champion-goal/run-b/champion-result.json`; `.tmp/bench/dev2000.json`; `.tmp/bench/heldout2000.json`
- **Fact**: `initial-seat-4-v9@00b1a655` wins 1,106/4,000 (27.650%) in the 4,000-game protocol; its seat-1 rate is 31.1%, with 557/2,000 dev and 549/2,000 held-out.
- **Implications**: A "more capable" variant must beat this on disjoint validation seeds, not merely on the selection grid.
- **Confidence**: High
- **Agent**: orchestrator prior analysis
- **Related**: HYP-001, D1-D4

### HYP-001: A more aggressive v9 mutation can beat v9 on held-out seeds

- **Claim**: At least one variant in an aggression grid over `pipWeight`, `doubleBonus`, `noFollowOnPenalty`, `endPipWeight`, `chainBonus`, and `endgamePipWeight` will produce a validation win rate above v9's, with a positive 95% lower bound on the difference.
- **Supporting OBS**: OBS-001
- **Why it matters**: If true, the system can evolve a more capable player; if false, the current v9 is at the local aggression optimum and the negative result is the finding.
- **Falsification strategy**: Run the grid on 910001/910002, validate top variants on 910003-910006, compute the two-proportion 95% CI; a lower bound <= 0 falsifies the claim.
- **Status**: Proposed
- **Confidence**: Medium

### TODO-001: Build the deterministic aggression search harness

- **Task**: Generate at least six aggression levels from the v9 source, compile and benchmark them on the selection seeds, write selection JSON.
- **Priority**: P0
- **Depends on**: nothing
- **Status**: Open
- **Related**: HYP-001, D1-D2

### TODO-002: Validate top variants on disjoint seeds and decide

- **Task**: Benchmark top 3 + v9 on seeds 910003-910006, compute the difference CI, persist the winner only if the lower bound is > 0.
- **Priority**: P0
- **Depends on**: TODO-001
- **Status**: Open
- **Related**: HYP-001, D3-D5

### RISK-001: Aggression parameters may overfit the selection seeds

- **Risk**: The grid winner on 910001/910002 may not generalize to 910003-910006.
- **Likelihood**: High
- **Impact**: High
- **Evidence**: Only 4,000 selection games; top variants are close.
- **Mitigation**: Disjoint validation seeds, 8,000 games per finalist, two-proportion CI; no promotion on selection alone.
- **Gate**: G3

### RISK-002: Aggressive mutations may fail to compile or may change more than intended

- **Risk**: String replacement of weights could corrupt the v9 source or replace the wrong occurrence.
- **Likelihood**: Medium
- **Impact**: High
- **Evidence**: The v9 source has repeated `score += 15`/`score += 20` patterns.
- **Mitigation**: Use a template with unique markers for the six target weights; assert each replacement count; compile every variant and report failures.
- **Gate**: G1

### OQ-001: Which parameterization best represents "aggression"?

- **Question**: Does increasing pip/double weight and decreasing follow-on safety/balance produce a monotonic aggression ladder?
- **Needed evidence**: A six-level grid with compile success and selection results; the shape of the win-rate curve.
- **Priority**: P1 important
- **Raised by**: Phase 0 orchestrator
- **Related**: HYP-001, RISK-002

### DEC-001: Use a scalar aggression ladder over six weights

- **Decision**: Generate levels by increasing `pipWeight` (3→8), `doubleBonus` (60→110), `endPipWeight` (7→12), and `endgamePipWeight` (2→7), while decreasing `noFollowOnPenalty` (100→25) and `chainBonus` (20→0).
- **Rationale**: Those six terms dominate the champion's risk/reward trade-off; a monotonic ladder answers "more and more aggressive" directly.
- **Alternatives considered**: Random mutation search (rejected: less interpretable, more overfit risk); full genetic algorithm (rejected: too wide for a four-round goal).
- **Related**: OQ-001, HYP-001
### OBS-002: Increasing aggression is monotonically worse

- **Source**: `.tmp/aggression-search/selection-results.json`; `.tmp/aggression-search/selection-control.stdout.txt`
- **Fact**: On the 4,000-game selection protocol, the aggressive ladder scored: a1 1,108/4,000 (27.700%), a2 1,095 (27.375%), a3 1,082 (27.050%), a4 1,066 (26.650%), a5 1,050 (26.250%). Each aggressive level was below v9 in the same field (v9 1,130–1,137). The only positive raw delta was a1's +2 wins over v9's separate 1,106 baseline, which is noise and field-shifted.
- **Implications**: "More and more aggressive" is the wrong direction for this champion; the aggressive frontier is downhill.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: HYP-001, EVD-002

### OBS-003: Calmer variants are the best candidates, but the evidence is borderline

- **Source**: `.tmp/aggression-search/validation-results.json`; `validation-extended.json`; `confirmation-910019.json`; `confirmation-910023.json`; `confirmation-910035.json`
- **Fact**: `control-c5` (pips 2, double 50, noFollow 140, end 6, chain 30, endgame 1) scored:
  - 8,000 held-out: 2,342/8,000 (29.275%) vs v9 2,264 (28.300%), diff +0.975pp, CI [−0.428, 2.378]
  - 24,000 extended: 6,962/24,000 (29.008%) vs v9 6,754 (28.142%), diff +0.867pp, CI [0.058, 1.675]
  - 8,000 fresh confirmation: 2,361/8,000 (29.513%) vs v9 2,317 (28.963%), diff +0.550pp, CI [−0.860, 1.960]
  - 24,000 large confirmation: 6,802/24,000 (28.342%) vs v9 6,700 (27.917%), diff +0.425pp, CI [−0.379, 1.229]
  - 96,000 final confirmation: 27,987/96,000 (29.153%) vs v9 27,598/96,000 (28.748%), diff +0.405pp, CI [−0.001, 0.811]
- **Implications**: c5 is consistently positive (+0.4 to +0.9pp), but the pre-registered 96,000-game final confirmation narrowly misses the 95% bar. It is a promising candidate, not a proven replacement.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: HYP-001, EVD-003..007

### VAL-001: HYP-001 is Falsified at the 95% bar

- **Target**: HYP-001
- **Method**: Ran the six-level aggressive grid, then a 12-level grid with calmer/controlled levels; validated top variants on disjoint seeds; added a pre-registered 96,000-game confirmation for the best calmer candidate.
- **Evidence examined**: `selection-results.json`; `validation-results.json`; `validation-extended.json`; `confirmation-910019.json`; `confirmation-910023.json`; `confirmation-910035.json`.
- **Result**: Falsified
- **Conclusion**: No aggressive variant beat v9 with a positive 95% lower bound; the best calmer candidate c5 has a +0.405pp point estimate but a 95% CI lower bound of −0.001 on 96,000 games. The correct verdict is "promising candidate, not statistically supported improvement"; v9 remains the champion.
- **Related**: HYP-001, OBS-002, OBS-003, EVD-007

### DEC-002: Do not promote c5 to the live champion

- **Decision**: Keep `control-c5` as a staged candidate in `.tmp/aggression-search/library-extended`; do not write it to `bones-data` or claim it is a proven replacement.
- **Rationale**: The final 96,000-game confirmation missed the pre-registered 95% lower-bound bar by 0.001pp; the point estimate is promising but not robust enough for a champion claim.
- **Alternatives considered**: Promote on the 24,000 positive result (rejected: superseded by larger independent confirmations); run more games until significant (rejected: post-hoc sequential testing).
- **Related**: OBS-003, VAL-001, EVD-007

### RISK-003: The c5 result is a borderline effect vulnerable to seed selection

- **Risk**: The +0.4pp c5 advantage may be a small true effect or residual noise; the 24,000 extended set crossed zero while the 96,000 final set did not.
- **Likelihood**: High
- **Impact**: Medium
- **Evidence**: EVD-003..007; final CI lower bound −0.001
- **Mitigation**: Report the full sequence, label c5 a candidate, and require a new pre-registered confirmation before promotion.
- **Gate**: G3

### OQ-002: Would a targeted c5 neighborhood or larger confirmation resolve the effect?

- **Question**: Is c5's +0.4pp effect real but small, and can a local search around c5 (or a 200k-game confirmation) clear the 95% bar?
- **Needed evidence**: A pre-registered independent confirmation with enough power for a 0.4pp effect (roughly 200k games per candidate), or a targeted neighborhood that produces a larger effect.
- **Priority**: P1 important
- **Raised by**: Round 4 orchestrator
- **Related**: RISK-003, OBS-003
