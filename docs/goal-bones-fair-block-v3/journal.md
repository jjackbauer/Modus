# Goal Journal — bones-fair-block-v3

> Append-only. Session executed against the contract in `goal.md`.

---

### OBS-001: The v3 harness rebuilds the incumbent byte-identically

- **Source**: `.tmp/fair-block-v3/specs/ceiling.json` (`champ-clone`), `policies/c5-newend.cs`, `policies/candidates/v2-cur2.cs`
- **Fact**: Generating `champ-clone` from `c5-newend.cs` with the six champion weight patterns plus the three-opponent block module produces a source whose SHA256 is `0CB1F7DC390AD77092FFFF87881BB7E680DB5695C844B877A66DB8EC2A83E539` — identical to the shipped `v2-cur2.cs`. At 200,000 discovery games both score exactly 61,086.
- **Implications**: The v3 chain restarts from a fully reproducible artifact, and the generator is a faithful description of the champion rather than an approximation of it.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-001, EVD-002

### OBS-002: The champion's edge is smaller on fresh seeds than on the v2 blocks

- **Source**: `sweep --candidates v2-cur2 --seedBase 910535 --seeds 100 --games 2000`
- **Fact**: On the never-before-used discovery block the champion scores 61,086/200,000 (30.5430%) against `fair-block` 58,899 (29.4495%), +1.0935pp, CI [0.8095, 1.3775]. The v2 selection and confirmation blocks had put the same comparison at +1.2640pp and +1.2365pp.
- **Implications**: Roughly 0.15pp of the v2 headline was block-specific luck. Adding a discovery block for tuning was the right call; the remaining +1.09pp is still far above the +0.50pp bar.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-003, RISK-001

### OBS-003: Full-information variants are worse than the champion

- **Source**: `ceiling --specs specs/ceiling.json --seedBase 910535 --seeds 100 --games 2000`
- **Fact**: `oracle-greedy` — a hand-designed policy that reads all four hands — scores 53,245/200,000 (26.6225%), which is 3.92pp *below* the champion. `oracle-truth-dist` — the champion's exact scoring rule with the probabilistic answer estimate replaced by the true per-opponent indicator — scores 60,495 (30.2475%), 0.30pp *below* the champion.
- **Implications**: Information is worth less than zero on this decision rule. The graded probability acts as a better-calibrated prior than a hard 0/1 indicator, and a badly-specified rule wastes perfect information entirely.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-004, EVD-005, DEC-001

### OBS-004: The champion's edge transfers to both field halves

- **Source**: `sweep --candidates v2-cur2 --half A|B --seedBase 910535 --seeds 100 --games 2000`
- **Fact**: On the 32-strategy half A the champion scores 62,666/200,000 (31.3330%) against 60,298 (30.1490%), +1.1840pp [0.8980, 1.4700]. On half B, 62,716 (31.3580%) against 60,025 (30.0125%), +1.3455pp [1.0597, 1.6313]. Absolute rates differ by ~1.7pp between halves while the delta is stable.
- **Implications**: The edge is not an artefact of particular opponents. Field composition moves absolute rates a lot and the champion-vs-incumbent delta very little.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-006, EVD-007

### OBS-005: One-at-a-time search is exhausted

- **Source**: `paired --specs specs/a1.json --seedBase 910535 --seeds 40 --games 2000`
- **Fact**: Across 43 single-knob alternatives the champion scored 24,448/80,000 (30.5600%) and the best alternative (`hold=6`) scored 24,492 — +0.055pp. Every knob moved the rate by less than ±0.06pp. Three knobs (`side`, `egflex`, `earlybal`) produced bit-identical results, i.e. they never change a decision.
- **Implications**: The champion is at a coordinate-wise optimum. The +0.30pp hypothesis for A1 cannot be reached this way.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-008, VAL-001

### OBS-006: Joint random perturbations also fail, and skew negative

- **Source**: `paired --specs specs/a1b.json --seedBase 910535 --seeds 40 --games 2000`
- **Fact**: Of 113 arms (110 random 5- and 10-knob perturbations plus three references), only 21 beat the champion. The best two (`R50`, `R68`) reached 24,511/80,000 (+0.0788pp) and the worst reached 23,850. The unpaired standard error on a difference at this size is 0.2303pp.
- **Implications**: The perturbation cloud is centred below the champion, not around it — consistent with a genuine local optimum rather than a search that has not been tried hard enough. A1 is closed.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-009, VAL-001

### OBS-007: A fair Monte-Carlo planner is roughly 10pp worse than the linear policy

- **Source**: `paired --specs … planner arms`; `probe --candidates planner-mc,planner-mc16,planner-low16`
- **Fact**: A fair planner that samples opponent hands from the exact unseen pool and rolls the round out with a greedy playout scores 20.4912% (4 samples), 20.8775% (16 samples) and 20.7275% (16 samples, low-pip playout) against the champion's 30.5600%. Quadrupling the sample count and inverting the playout preference each move the rate by less than 0.4pp.
- **Implications**: The playout policy, not the sample count, determines planner quality, and a greedy playout is far too weak. A champion-strength playout would cost roughly two orders of magnitude more time than the measurement budget allows.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-010, VAL-002

### OBS-008: The exact tempo signal adds nothing and the near-tie band hurts monotonically

- **Source**: `paired --specs specs/a3.json` and `specs/a4.json --seedBase 910535 --seeds 40 --games 2000`
- **Fact**: An exact expected-free-turns term — `E[turns] = (1−P(h₂ answers)) + (1−P(h₂+h₃ answers)) + [m=0]`, where the union of two disjoint hands is a uniformly random subset of the summed size — peaks at +0.0475pp (`freeturns=80`) and is negative at 220. Preferring higher-pip moves within a near-tie band degrades monotonically: −0.1075pp at band 8, −1.78pp at 20, −3.58pp at 40.
- **Implications**: The existing graded block term already encodes all the tempo information the exact computation provides, and deterministic tie-breaking at the strict maximum is already the right rule.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-011, EVD-012, VAL-003, VAL-004

### OBS-009: The champion replicates on three fresh seed blocks

- **Source**: `sweep --candidates v2-cur2 --seedBase 910635 | 910735 | 910835`
- **Fact**: Selection 61,504/200,000 (30.7520%) vs 58,854 (29.4270%), +1.3250pp [1.0408, 1.6092]; confirmation 61,268 (30.6340%) vs 58,626 (29.3130%), +1.3210pp [1.0371, 1.6049]; final 122,866/400,000 (30.7165%) vs 118,108 (29.5270%), +1.1895pp [0.9884, 1.3906].
- **Implications**: The v2 champion's edge is real and stable on seeds that no v2 tuning ever touched — six fresh blocks in total now, all with lower bound > 0.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-013, EVD-014, EVD-015

### HYP-001: Residual search finds ≥ +0.30pp over `v2-cur2`

- **Claim**: A second-order search escapes the coordinate-wise optimum found in v2.
- **Supporting OBS**: OBS-002
- **Why it matters**: It is the cheapest possible improvement path.
- **Falsification strategy**: One-at-a-time sweep plus 110 joint random perturbations on a dedicated discovery block, then verify any winner on a separate selection block.
- **Status**: Falsified
- **Confidence**: High

### HYP-002: A budgeted Monte-Carlo planner beats the linear evaluation

- **Claim**: Sampling opponent hands from the exact unseen pool and rolling out improves move selection enough to gain ≥ +0.50pp.
- **Supporting OBS**: OBS-003
- **Why it matters**: It is the only qualitatively different decision process available.
- **Falsification strategy**: Build it, measure throughput, and compare at 200,000 games; vary sample count and playout preference.
- **Status**: Falsified
- **Confidence**: High

### HYP-003: An exact tempo signal (expected free turns) beats the graded block probability

- **Claim**: Replacing per-opponent answer probabilities with the exact expected number of consecutive extra turns improves the policy.
- **Supporting OBS**: OBS-008
- **Why it matters**: The current term is a heuristic shape; the free-turns expression is exact and differently shaped.
- **Falsification strategy**: Sweep its weight on the champion base at 80,000 paired games.
- **Status**: Falsified
- **Confidence**: High

### HYP-004: Near-tie mixing improves the policy

- **Claim**: Preferring higher-pip moves within a score band δ of the maximum is worth ≥ +0.10pp.
- **Supporting OBS**: OBS-008
- **Why it matters**: v2 only tested last-wins; the band was untested.
- **Falsification strategy**: Sweep δ at 80,000 paired games; expect monotone degradation if the strict maximum is already correct.
- **Status**: Falsified
- **Confidence**: High

### HYP-005: The fair information ceiling is materially above the champion

- **Claim**: Perfect knowledge of opponent hands raises the achievable rate well above 30.72%.
- **Supporting OBS**: OBS-003
- **Why it matters**: If true, the remaining headroom is information-shaped and worth pursuing; if false, it is decision-rule-shaped.
- **Falsification strategy**: Measure a hand-designed full-info policy and a truth-fused version of the champion rule.
- **Status**: Falsified (the gap is negative)
- **Confidence**: High

### HYP-006: The champion's edge is field-specific

- **Claim**: The +1.09…+1.26pp edge is an artefact of the 64-strategy field.
- **Supporting OBS**: OBS-004
- **Why it matters**: A field-specific edge would not be a real champion.
- **Falsification strategy**: Split the field deterministically in half and measure the delta on each half.
- **Status**: Falsified (the edge holds on both halves)
- **Confidence**: High

### VAL-001: A1 residual search

- **Target**: HYP-001
- **Method**: 43 single-knob alternatives and 110 joint random perturbations, all paired on the discovery block at 80,000 games.
- **Evidence examined**: OBS-005 (best single +0.055pp), OBS-006 (best joint +0.0788pp, 21/113 beating the champion, unpaired SE 0.2303pp).
- **Result**: Falsified.
- **Conclusion**: The champion is at a local optimum in this parameterization. Nothing here is within a factor of six of the +0.50pp bar.
- **Related**: OBS-005, OBS-006

### VAL-002: A2 Monte-Carlo planner

- **Target**: HYP-002
- **Method**: A fair planner sampling opponent hands from the exact unseen pool, rolling out with a greedy playout; measured at 4 and 16 samples and with an inverted playout preference.
- **Evidence examined**: OBS-007 (20.49%, 20.88%, 20.73% vs 30.56%).
- **Result**: Falsified.
- **Conclusion**: Greedy playouts are too weak for the planner to be competitive, and a champion-strength playout is out of reach for the measurement budget. The approach is closed on cost grounds as well as quality.
- **Related**: OBS-007

### VAL-003: A3 exact tempo signal

- **Target**: HYP-003
- **Method**: Added an exact expected-free-turns term and swept its weight 40/80/140/220 on the champion base, 80,000 paired games.
- **Evidence examined**: OBS-008 (24,442 / 24,486 / 24,449 / 24,427 vs 24,448).
- **Result**: Falsified.
- **Conclusion**: The exact signal carries no information the graded block term lacks.
- **Related**: OBS-008

### VAL-004: A4 near-tie mixing

- **Target**: HYP-004
- **Method**: Prefer the higher-pip move whenever its score is within δ of the running maximum; δ ∈ {8, 20, 40}; 80,000 paired games. A first attempt was discarded because it compared against the last accepted score rather than the running maximum, which drifted the choice downward — recorded as a tooling defect, not a result.
- **Evidence examined**: OBS-008 (24,362 / 24,014 / 23,574 vs 24,448).
- **Result**: Falsified.
- **Conclusion**: Strict-maximum selection is already correct; band preferences are strictly harmful.
- **Related**: OBS-008, RISK-002

### VAL-005: A5 fair information ceiling

- **Target**: HYP-005
- **Method**: Two diagnostics — a hand-designed full-information greedy policy, and the champion's rule with the answer estimate replaced by the true per-opponent indicator.
- **Evidence examined**: OBS-003.
- **Result**: Falsified. Both full-information variants are *below* the champion, by 3.92pp and 0.30pp respectively.
- **Conclusion**: The remaining headroom is decision-rule-shaped, not information-shaped. This also explains why every prior information-based refinement (v2's oracle-block, void memory) failed.
- **Related**: OBS-003

### VAL-006: A6 field transfer

- **Target**: HYP-006
- **Method**: Deterministic ordinal-parity split of the 64 historical strategies; champion and `fair-block` measured on each half at 200,000 games.
- **Evidence examined**: OBS-004.
- **Result**: HYP-006 Falsified — the edge survives on both halves (+1.1840pp, +1.3455pp).
- **Caveat**: both arms were tuned on the full 64, so neither half is a clean holdout for either; only the *delta* is a fair comparison. Recorded as OQ-001.
- **Related**: OBS-004

### DEC-001: Do not pursue information-side improvements further

- **Decision**: Stop treating better opponent-hand estimates as an improvement path.
- **Rationale**: EVD-004 and EVD-005 — a truth-fused variant of the champion's own rule loses 0.30pp, and a hand-designed full-information rule loses 3.92pp. Combined with v2's oracle-block result, four independent full-information variants now sit at or below the fair champion.
- **Alternatives considered**: Sharper void tracking (already falsified in v2), exact blocked-count with true distribution (falsified here), a full-info planner (too slow to measure).
- **Related**: OBS-003, VAL-005

### DEC-002: Declare a bounded negative rather than promote a noise-level variant

- **Decision**: `v2-cur2` remains champion. The best v3 variant (`freeturns=80`, +0.0475pp) is not promoted.
- **Rationale**: The bar is +0.50pp; the best finding is a factor of ten short and inside the paired interval.
- **Alternatives considered**: Promoting `freeturns=80` on the strength of its discovery-block number — rejected as exactly the overfitting failure the v2 risk register flagged.
- **Related**: OBS-008, RISK-001

### DEC-003: Report the replication as the session's positive result

- **Decision**: Present the champion's fresh-block replication (three blocks, lower bound > 0 on all three) as the substantive positive finding.
- **Rationale**: It is a genuine independent confirmation on seeds no v2 tuning touched, and it is the only claim the data supports at full strength.
- **Alternatives considered**: Reporting nothing positive — rejected because the replication is real and material.
- **Related**: OBS-009

### RISK-001: Discovery-block tuning still selects on noise

- **Risk**: 153 arms were screened on the discovery block; the best of them is biased upward.
- **Likelihood**: High
- **Impact**: Low here
- **Evidence**: OBS-006 (best of 113 joint arms = +0.0788pp while the pairwise SE is ~0.11pp)
- **Mitigation**: No variant was promoted, so the bias costs nothing. The replication used a disjoint selection block.
- **Gate**: —

### RISK-002: First near-tie test was invalid

- **Risk**: The initial A4 implementation compared a candidate score against the last *accepted* score rather than the running maximum, so the choice drifted downward by up to δ per move and produced a meaningless 19.10%.
- **Likelihood**: Realised
- **Impact**: Low
- **Evidence**: VAL-004
- **Mitigation**: Re-implemented against the running maximum and re-measured; the corrected result is a monotone loss, so the conclusion (reject) is unchanged.
- **Gate**: —

### OQ-001: Neither field half is a clean holdout for the champion

- **Question**: Does the champion's edge survive against *entirely unseen strategies* rather than unseen halves of a field both arms were tuned on?
- **Needed evidence**: A benchmark bank that post-dates the championships; none exists in this repository.
- **Priority**: P1
- **Raised by**: Round 3, orchestrator
- **Related**: OBS-004

### OQ-002: Where does the remaining 5.7pp over chance come from?

- **Question**: The champion is 30.72% against a 25% baseline. Since information is worthless and weights are exhausted, which feature class carries the edge?
- **Needed evidence**: Ablation of the champion's score terms on a fresh block — remove each term and measure the loss.
- **Priority**: P2
- **Raised by**: Round 8, orchestrator
- **Related**: OBS-003, OBS-005

### OQ-003: Would a champion-strength playout make the planner competitive?

- **Question**: A1's planner used a greedy playout. Would replaying the round with the champion's own evaluation (roughly 100× slower) recover the ~10pp gap?
- **Needed evidence**: A single-seed throughput measurement plus a 20,000-game screen; not attempted.
- **Priority**: P2
- **Raised by**: Round 5, orchestrator
- **Related**: OBS-007
