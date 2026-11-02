# Goal Journal — bones-fair-block

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---

### OBS-001: The champion ignored the next player entirely

- **Source**: `.tmp/aggression-search/library-fair/.bones/strategy-library/seat-1.json`; `WIP/Wip.Bones/Domain/BonesGameTypes.cs:75-88,146-174`
- **Fact**: `c5-newend` scores only its own hand, board ends, and chain; it never estimates whether the next player can answer. `BonesRoundState` exposes the public `EventLog` and board, and `BonesHands` also happens to expose all hands to scripts.
- **Implications**: A public-information blocking term is the highest-leverage missing capability; hidden-hand access would be an oracle, not a fair player.
- **Confidence**: High
- **Agent**: orchestrator
- **Related**: HYP-001, EVD-001

### HYP-001: A fair next-player blocker beats c5-newend

- **Claim**: Adding a public-information estimate of whether the next player can match either new end will raise the fair fixed-field win rate above `c5-newend` by at least +0.3pp, with a positive 95% lower bound.
- **Supporting OBS**: OBS-001
- **Why it matters**: It addresses the main weakness without cheating.
- **Falsification strategy**: Fair fixed-field selection and confirmation, 200k games each; if either lower bound ≤0, falsify.
- **Status**: Supported
- **Confidence**: High

### VAL-001: HYP-001 is Supported on two independent 200k sets

- **Target**: HYP-001
- **Method**: Built `fair-block` from `c5-newend`; added a `NextMatchProbability` term using own hand, board length, event log, and next-seat hand-size inference; excluded candidates from the fixed field; ran 100 seeds × 2,000 games on selection and disjoint confirmation seeds.
- **Evidence examined**: `block-results.json`; `block-confirmation.json`; source inspection; live hashes.
- **Result**: Supported
- **Conclusion**: fair-block 59,107/200,000 (29.553%) vs c5 58,150 (29.075%), diff +0.478, CI [0.196, 0.761]; confirmation 58,862/200,000 (29.431%) vs c5 58,060 (29.030%), diff +0.401, CI [0.119, 0.683]. Combined 117,969/400,000 (29.4923%) vs 116,210 (29.0525%), diff +0.4398, CI [0.2403, 0.6392].
- **Related**: HYP-001, EVD-001..003

### DEC-001: Promote fair-block to staged champion; discard oracle as a champion

- **Decision**: Stage `fair-block`; keep `oracle-block` only as a diagnostic result.
- **Rationale**: `oracle-block` used hidden opponent hands and did not beat `fair-block` (+0.102 vs c5, CI includes zero); the fair version is stronger and legitimate.
- **Alternatives considered**: Promote oracle (rejected: hidden information, no gain); keep c5-newend (rejected: fair-block is significantly better).
- **Related**: VAL-001, EVD-004

### RISK-001: The block probability model is approximate

- **Risk**: The union approximation for two ends and the independent-hand approximation may misprice blocking; it still won in two independent tests.
- **Likelihood**: Medium
- **Impact**: Medium
- **Evidence**: EVD-001..003
- **Mitigation**: Keep the term soft (max ±48), validate on 400k fixed-field games; do not tune further on the same seeds.
- **Gate**: G2

### OQ-001: Can a full opponent model beat the soft blocker?

- **Question**: Would modeling all three opponents, or using exact public tile-count combinations, produce a larger gain than the next-player approximation?
- **Needed evidence**: A new variant with a multi-opponent block term, tested on fresh fixed-field seeds.
- **Priority**: P1 important
- **Raised by**: Round 3 orchestrator
- **Related**: RISK-001, EVD-003