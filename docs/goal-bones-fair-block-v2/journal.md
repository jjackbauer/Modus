# Goal Journal — bones-fair-block-v2

> Append-only. See `stretch-goal-harness/references/evidence-protocol.md` for entry format.

---

### OBS-001: `fair-block` is `c5-newend` plus exactly one added term

- **Source**: `.tmp/block-search/library-confirm/.bones/strategy-library/seat-1.json`, `.tmp/aggression-search/library-fair/.bones/strategy-library/seat-1.json`
- **Fact**: The incumbent source is 16,398 characters and the `c5-newend` base is 13,517. The only behavioural addition is a pre-`if (score > bestScore)` block computing new ends and adding `round((0.60 − NextMatchProbability(...)) × 80)`. It models only the **next** player.
- **Implications**: The champion is a single delta over the previous champion, so any improvement must come either from a better block model or from the base terms the champion inherited unexamined.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-001

### OBS-002: Round dynamics — the match is almost always decided in one round

- **Source**: `dotnet run --project .tmp/fair-block-v2/FairBlockV2.csproj -c Release -- diag --seeds 20 --games 2000`
- **Fact**: Over 40,000 matches: 43,708 rounds, 1.093 rounds/match; 33.25% of rounds end blocked; mean points per round 26.77 (36.49 blocked, 21.92 domino) against a 12-point target; mean 26.66 moves per round.
- **Implications**: Match win rate ≈ round win rate, so the objective is round-win probability weighted by points. A 12-point target with 26.77 mean points means round 1 usually ends the match.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-019, HYP-001

### OBS-003: The double-six set totals 168 pips, not 147

- **Source**: `WIP/Wip.Bones/Engine/BonesGameEngine.cs:301-311` (`CreateDoubleSixSet`), `BonesGameEngine.cs:115-134` (`ScoreRound`)
- **Fact**: Enumerating all 28 tiles with a ≤ b ≤ 6 gives Σ(a+b) = 168, because a double contributes its pip twice. Using 147 in the diagnostic produced negative "winner pips", which is impossible since `ScoreRound` awards the winner the sum of the other three hands.
- **Implications**: Every remaining-pip computation must use 168. This is the constant the fair policy needs for exact opponent pip totals.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-002

### OBS-004: Public tile counting is exact, not approximate

- **Source**: `.tmp/fair-block-v2/Audit.cs` (`OpponentMatchCount` vs `TrueOpponentMatchCount`), command `diag --seeds 20 --games 2000`
- **Fact**: For pip p, `unseen[p] = 7 − myHandPips[p] − playedPips[p]` equals the true number of opponent tiles containing p, because all 28 tiles are dealt and none are discarded. Across 292,160 decisions the public count of opponent tiles answering either new end disagreed with the full-knowledge count **0 times**.
- **Implications**: The block model can be exact rather than probabilistic. `fair-block`'s double-counting of the connecting tile is the only real error in its arithmetic — and it is small.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-003, HYP-002

### OBS-005: The graded block term saturates at about +0.2pp over `fair-block`

- **Source**: `sweep --specs specs/stageB.json --seeds 40 --games 2000`; `sweep --specs specs/stageD.json --seeds 40 --games 2000`
- **Fact**: Single-opponent and three-opponent graded terms, over weights 80–300 and eight opponent weight shapes, all land between 29.65% and 29.75% against `fair-block` 29.5438% (80,000 games). The offset `(offset − p)` has no effect at all, because a constant added to every move cannot change the argmax.
- **Implications**: Approaches 1 and 2 are real but small. A further refinement of the block probability cannot deliver the +0.5pp target on its own.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-005, VAL-001, DEC-001

### OBS-006: A perfect-information block oracle was *worse* than the approximate term

- **Source**: `.tmp/block-search/block-confirmation.stdout.txt`
- **Fact**: In the prior campaign the oracle variant scored 58,264/200,000 (29.132%) versus `fair-block` 58,862 (29.431%) on the same seeds.
- **Implications**: More information about the next player's ability is not the lever. The graded pressure itself, not literal block detection, is what helps. This predicted that approach 5 (better probability estimates) would also fail — and it did.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-016, HYP-003

### OBS-007: Same-deal paired evaluation is ~3.3× sharper than the unpaired interval

- **Source**: `paired --specs specs/dbl.json --seeds 20 --games 2000`
- **Fact**: Both arms see identical deals because round seeds derive from the match seed and round number only (`BonesMatchSimulator.cs:130-131`). At 40,000 games the paired McNemar interval half-width is ~0.19pp versus ~0.63pp unpaired; at 80,000 games the paired standard error is 0.069pp.
- **Implications**: Search can be run on 80,000-game screens with paired statistics while formal claims still use the prescribed unpaired two-proportion interval.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-004, DEC-002

### OBS-008: The base terms were badly mis-weighted, and the block term only pays off once they are fixed

- **Source**: `sweep --specs specs/decomp.json --seeds 100 --games 2000`
- **Fact**: On the 200,000-game selection set: `c5-newend` 58,132 (29.0660%), `fair-block` 59,073 (29.5365%), multi-opponent block term alone 59,357 (29.6785%, +0.1420pp), weight corrections alone 59,753 (29.8765%, +0.3400pp), both together 61,601 (30.8005%, +1.2640pp).
- **Implications**: The two changes are strongly super-additive. The block term is nearly worthless while the base over-penalises doubles and mis-selects sides; it becomes valuable once those are corrected. A single-knob sweep on the unmodified base would have missed this.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-013

### HYP-001: Round-win probability, not match management, is the objective

- **Claim**: Because 1.093 rounds decide a 12-point match and the mean round is worth 26.77 points, a policy that maximises round-win probability maximises match-win probability.
- **Supporting OBS**: OBS-002
- **Why it matters**: If true, all effort belongs in per-move round value; if false, endgame match-state reasoning would be required.
- **Falsification strategy**: Look for matches with ≥3 rounds and check whether the candidate's per-round win rate differs materially from its match win rate.
- **Status**: Supported
- **Confidence**: High

### HYP-002: Exact public counting will beat the approximate union

- **Claim**: Replacing `fair-block`'s approximate match-tile union with an exact count (subtracting the connecting tile when it is unseen, collapsing equal ends) improves round win rate.
- **Supporting OBS**: OBS-004
- **Why it matters**: If true, the block model is arithmetic-limited and fixable; if false, the approximation cost is negligible.
- **Falsification strategy**: Measure the exact-count three-opponent variant against the approximate one on identical seeds with the paired interval.
- **Status**: Falsified (as a standalone lever)
- **Confidence**: High

### HYP-003: Pass-derived void memory will sharpen the block model enough to matter

- **Claim**: A player who passed while pip p was open holds no tile containing p for the rest of the round, so tracking voids materially improves the answer probabilities.
- **Supporting OBS**: OBS-006, OBS-005
- **Why it matters**: This is the only remaining source of genuinely new public information.
- **Falsification strategy**: Implement void-blended per-opponent answer probabilities and measure at 200,000 games.
- **Status**: Falsified
- **Confidence**: High

### HYP-004: A guaranteed-silent continuation chain is a large edge

- **Claim**: Because `unseen[p]` is invariant for the whole round, the set of "dead" pips is fixed and knowable; a search over silent continuations should find positions where the candidate plays out its hand unopposed.
- **Supporting OBS**: OBS-004
- **Why it matters**: A guaranteed round win would be worth far more than any weight tweak.
- **Falsification strategy**: Implement a budgeted depth-first silent-chain search and measure; if the win delta is inside the paired noise band, the mechanism does not occur often enough to matter.
- **Status**: Falsified
- **Confidence**: High

### HYP-005: The deterministic tie-break is exploitable

- **Claim**: The policy's strict `>` comparison means the first legal move wins every tie; a different tie-break would change results materially.
- **Supporting OBS**: OBS-001
- **Why it matters**: If ties are frequent, the champion is fragile; if not, this is a non-issue.
- **Falsification strategy**: Change the comparison to `>=` (last legal move wins ties) and measure at 200,000 games.
- **Status**: Falsified (ties are not load-bearing for the champion)
- **Confidence**: High

### VAL-001: Block-model refinements (approaches 1 and 2)

- **Target**: HYP-002, round-2 claim batch
- **Method**: Generated three-opponent graded variants from `c5-newend` inside the harness generator, verified the generator reproduces `fair-block` bit-exactly, then swept weight and shape on 80,000 paired games.
- **Evidence examined**: `repro-next80` 11,832/40,000 with mean score differential −11.815, identical to `fair-block`; `multi80` 11,854; the 80,000-game shape sweep spanning 23,714–23,798 wins against `fair-block` 23,635.
- **Result**: Supported as a mechanism, Falsified as a lever large enough for the target. The multi-opponent exact term is worth about +0.10 to +0.19pp over the incumbent.
- **Conclusion**: Approaches 1 and 2 are real but an order of magnitude too small. Retained them as part of the champion and moved on.
- **Related**: OBS-005, EVD-004, EVD-005

### VAL-002: Silent-chain planner (approach 4)

- **Target**: HYP-004
- **Method**: Emitted a `SilentChain` depth-first search with a node budget; required every intermediate end to be a "dead" pip; scored the chain length and a flat bonus for playing out the whole hand; measured at 80,000 paired and 200,000 unpaired games.
- **Evidence examined**: `mx120-chainwin200/600/2000/20000` all identical to the base at 80,000 games; at 200,000 games `a4-chaindep45` and `a4-chainwin600` both scored 61,620 versus the champion 61,601.
- **Result**: Falsified. The delta is +19 wins in 200,000 (+0.0095pp), inside the interval; `bxChainWin` never fired, so both variants reduce to the same small chain-length bonus.
- **Conclusion**: Both-ends-dead positions are too rare for the planner to matter. The mechanism is exact but not load-bearing.
- **Related**: OBS-004, EVD-015

### VAL-003: Seat-aware policy (approach 3)

- **Target**: round-5 claim batch
- **Method**: Scaled the block module by seat (seats 1–2 versus 3–4) at four splits and measured on the rotating-seat field at 200,000 games.
- **Evidence examined**: 70/130 → 61,561; 50/150 → 61,450; 130/70 → 61,416; 150/50 → 61,382; champion 61,601.
- **Result**: Falsified. Every split is at or below the champion, and the damage grows with the extremity of the split.
- **Conclusion**: Seat-conditional weighting does not help on a field where every strategy rotates through all four seats.
- **Related**: EVD-014

### VAL-004: Pass-derived void memory (approach 5)

- **Target**: HYP-003
- **Method**: Replayed the event log to reconstruct the board ends at each pass, recorded per-seat voids, and blended the void-adjusted match count into each opponent's answer probability; measured at 200,000 games.
- **Evidence examined**: `a5-voids100` 61,314 (30.6570%); `a5-voids50` 61,496 (30.7480%); champion 61,601.
- **Result**: Falsified. Full void conditioning loses about 0.14pp; half strength loses about 0.04pp.
- **Conclusion**: Consistent with OBS-006 — sharper answer probabilities do not convert into wins. Recorded as rejected.
- **Related**: OBS-006, EVD-016

### VAL-005: Deterministic tie-break audit (approach 6)

- **Target**: HYP-005
- **Method**: Replaced the strict `if (score > bestScore)` with `>=` so the last legal move wins ties, then measured at 200,000 games.
- **Evidence examined**: `a6-tiebreak-last` 61,522 (30.7610%) versus champion 61,601 (30.8005%).
- **Result**: Falsified. The alternative tie-break is slightly worse, so the fixed tie-break is not a source of free wins and is not exploitable against the champion in the direction tested.
- **Conclusion**: Ties exist but are not load-bearing. A mixed tie-break was not pursued.
- **Related**: EVD-017

### VAL-006: The champion clears all three pre-registered bars

- **Target**: D1–D4
- **Method**: Measured the frozen champion spec on three disjoint seed sets with the prescribed unpaired interval, then re-ran the final set.
- **Evidence examined**: Selection 61,601/200,000 (30.8005%) vs 59,073 (29.5365%), +1.2640pp [0.9795, 1.5485]; confirmation 61,505 vs 59,032, +1.2365pp [0.9521, 1.5209]; final 123,068/400,000 vs 118,161, +1.2267pp [1.0256, 1.4279]; the final set reproduced 123,068 exactly on a second run.
- **Result**: Supported.
- **Conclusion**: `v2-cur2` is a confirmed champion under the mission's definition.
- **Related**: EVD-006, EVD-007, EVD-008, EVD-009

### DEC-001: Keep the surviving block model in the champion rather than dropping it

- **Decision**: The champion keeps the three-opponent graded block term (weight 120, opponent weights 1.0 / 0.5 / 0.25).
- **Rationale**: Although it is worth only +0.142pp alone (EVD-013), it contributes most of the super-additive gain once the base weights are corrected; without it the corrected base reaches only +0.340pp.
- **Alternatives considered**: Dropping the block term (loses ~0.92pp); single-opponent next-player term (0.10pp worse than three-opponent); oracle binary term (worse, OBS-006).
- **Related**: EVD-013, OBS-008

### DEC-002: Use paired evaluation for search, unpaired for claims

- **Decision**: All search and screening uses same-deal paired McNemar intervals; every reported claim uses the prescribed unpaired two-proportion interval.
- **Rationale**: Arms share identical deals (EVD-004), so the paired estimator is valid and about 3.3× sharper; the unpaired interval is conservative and is what the mission specifies for verdicts.
- **Alternatives considered**: Unpaired-only search — rejected as it cannot resolve 0.2pp effects on affordable samples.
- **Related**: OBS-007

### DEC-003: Do not re-tune on the confirmation or final seeds

- **Decision**: The champion spec was frozen after the selection sweep; confirmation and final runs used the identical spec with no further edits.
- **Rationale**: Mission rule; also the reason the three independent estimates agree to within 0.04pp.
- **Alternatives considered**: Using confirmation feedback to adjust weights — rejected as it would destroy the confirmation's independence.
- **Related**: EVD-006, EVD-007, EVD-008

### DEC-004: Reject approaches 3–6 and promote the approach-1/2 champion

- **Decision**: `v2-cur2` is the champion; approaches 3, 4, 5 and 6 are recorded as rejected with 200,000-game measurements.
- **Rationale**: Every alternative scored at or below the champion (EVD-014..017). None reached the +0.5pp bar.
- **Alternatives considered**: Adding the silent-chain bonus (+0.0095pp) — rejected as noise for added complexity.
- **Related**: EVD-014, EVD-015, EVD-016, EVD-017

### RISK-001: Selection-set overfitting through repeated search

- **Risk**: ~120 arms were screened on the selection seeds; the maximum of many noisy estimates is biased upward.
- **Likelihood**: High
- **Impact**: High
- **Evidence**: EVD-006 vs EVD-007 vs EVD-008
- **Mitigation**: Selection, confirmation and final estimates agree within 0.04pp (+1.2640, +1.2365, +1.2267), and the final 400,000-game lower bound is +1.0256pp — far above any plausible bias.
- **Gate**: —

### RISK-002: The paired estimator could mask a systematic deal-order artefact

- **Risk**: Paired evaluation assumes both arms see the same deals; if opponent slots consumed shared state, the pairing would be invalid.
- **Likelihood**: Low
- **Impact**: Medium
- **Evidence**: `BonesMatchSimulator.cs:130-131`, `.tmp/fair-block-v2/Program.cs` (`RunGameWon`)
- **Mitigation**: The replicated `RunGameWon` reproduced `fair-block`'s Arena count exactly (11,832/40,000), so the runner and pairing are faithful; all verdicts use the unpaired interval anyway.
- **Gate**: —

### RISK-003: The champion is tuned against this specific field

- **Risk**: Weight corrections found on a 69-entry field may not transfer to unseen opponents.
- **Likelihood**: Medium
- **Impact**: Medium
- **Evidence**: EVD-006
- **Mitigation**: The winning changes are qualitative (stop penalising doubles, consume the end you can least exploit), not opponent-identity specific; the confirmation field is the same but the seeds are disjoint. Not tested against unseen strategies — recorded as OQ-002.
- **Gate**: —

### OQ-001: Does the champion still beat `fair-block` on a completely different field?

- **Question**: Would the +1.23pp survive replacing the 64 historical strategies with a fresh opponent set?
- **Needed evidence**: A new benchmark bank; none exists in this repo.
- **Priority**: P1
- **Raised by**: Round 5, orchestrator
- **Related**: RISK-003

### OQ-002: Why does the block term only pay off after the base correction?

- **Question**: Is the super-additivity causal (the block term needs a base that does not waste doubles) or an artefact of the specific weights?
- **Needed evidence**: A 2×2 decomposition at a third base weight setting.
- **Priority**: P2
- **Raised by**: Round 6, orchestrator
- **Related**: EVD-013

### OQ-003: Can the remaining ~1.5pp to a hypothetical ceiling be reached fairly?

- **Question**: The champion is 30.77% against a 25% chance baseline. How much of the remainder is reachable with public information only?
- **Needed evidence**: A full-knowledge reference policy to bound the information value; not built here.
- **Priority**: P2
- **Raised by**: Round 5, orchestrator
- **Related**: OBS-006

### OBS-009: The champion carries two unused private helpers

- **Source**: `.tmp/fair-block-v2/policies/candidates/v2-cur2.cs`; `Select-String -Pattern "SilentChain\(|BlockWinProbability\("`
- **Fact**: `SilentChain` appears 4 times (its declaration plus its own three recursive calls) and `BlockWinProbability` once (its declaration only). Neither has an external call site, because the generator emits the full helper set while `ChainWeight`, `ChainWin` and `Gate` are all zero for this variant.
- **Implications**: The champion source is ~1.3k characters longer than necessary. The dead code is unreachable, so the measured behaviour is exactly the shipped behaviour — but it is recorded here rather than quietly trimmed, because trimming would change the source hash that EVD-010 and EVD-020 verified against the measured artifact.
- **Confidence**: High
- **Agent**: Orchestrator
- **Related**: EVD-010, EVD-020, ART-006
