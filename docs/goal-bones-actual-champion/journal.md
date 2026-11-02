# Goal Journal — bones-actual-champion

> Append-only. See the stretch-goal-harness evidence protocol for entry format.

---

### OBS-001: The aggressive direction was falsified; a calmer, corrected variant wins

- **Source**: `.tmp/aggression-search/fair-results.json`; `.tmp/aggression-search/fair-confirmation.json`
- **Fact**: `c5-newend` — c5's calmer weights (pips 2, double 50, noFollow 140, end 6, chain 30, endgame 1) plus the corrected new-end follow-on counter — beat v9 on two independent 200,000-game fixed-field tests.
- **Implications**: The actual champion is not more aggressive; it is calmer and fixes the stale-end follow-on count. Aggression was the wrong lever.
- **Confidence**: High
- **Agent**: orchestrator experiment
- **Related**: HYP-001, EVD-001..003

### HYP-001: c5-newend is a real champion, not a selection artifact

- **Claim**: On a fixed field that excludes the candidate and v9, `c5-newend` beats v9 by at least +0.5 percentage points with a 95% lower bound > 0, on disjoint selection and confirmation sets.
- **Supporting OBS**: OBS-001
- **Why it matters**: This is the difference between a promising candidate and an actual champion.
- **Falsification strategy**: Run two fixed-field 200k-game sets on disjoint seeds; compute the two-proportion CI; if either lower bound ≤0, falsify.
- **Status**: Supported
- **Confidence**: High

### VAL-001: HYP-001 is Supported on two independent 200k sets

- **Target**: HYP-001
- **Method**: Excluded v9 and all candidates from the 64-strategy field; compiled each candidate separately; ran 100 seeds × 2,000 games per candidate; repeated on a disjoint 100-seed set.
- **Evidence examined**: `fair-results.json`; `fair-confirmation.json`; library reload; live hashes.
- **Result**: Supported
- **Conclusion**: `c5-newend` 58,150/200,000 (29.075%) vs v9 57,024 (28.512%), diff +0.563, CI [0.282, 0.844]; confirmation 58,060/200,000 (29.030%) vs v9 56,839 (28.419%), diff +0.611, CI [0.330, 0.891]. Combined 116,210/400,000 (29.0525%) vs 113,863 (28.4658%), diff +0.5867, CI [0.388, 0.785]. All lower bounds > 0.
- **Related**: HYP-001, EVD-001..003

### DEC-001: Stage c5-newend; do not write live data

- **Decision**: Persist `c5-newend` to `.tmp/aggression-search/library-fair`; leave `bones-data` untouched.
- **Rationale**: The champion is validated, but live promotion is a separate data-changing operation; the user's workspace data must not be overwritten implicitly.
- **Alternatives considered**: Write directly to `bones-data` (rejected: destructive without explicit permission); keep only in memory (rejected: not durable).
- **Related**: VAL-001, EVD-004, EVD-005

### RISK-001: Multiple candidate comparisons and seed selection can bias the winner

- **Risk**: `c5-newend` was selected among five fair candidates; the selection set could favor it.
- **Likelihood**: Medium
- **Impact**: High
- **Evidence**: fair selection and confirmation use disjoint seed sets; both CIs exclude zero; the effect is +0.56/+0.61.
- **Mitigation**: Independent 200k confirmation, fixed field excluding candidates, report both sets and the pooled CI.
- **Gate**: G2

### OQ-001: Should the staged champion be applied live?

- **Question**: Should `c5-newend` replace the live `seat-1.json` in `bones-data`?
- **Needed evidence**: Explicit user permission and a backup/restore plan; the search harness intentionally does not write live data.
- **Priority**: P1 important
- **Raised by**: Round 3 orchestrator
- **Related**: DEC-001