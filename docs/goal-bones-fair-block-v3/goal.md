# Goal — bones-fair-block-v3

> **Status**: ADOPTED AND EXECUTED (8 rounds, 2026-11-01). Outcome: bounded negative — no variant
> beat `v2-cur2`. See `state.md` for the round record and `journal.md` for the evidence trail.
> The harness memory (`journal.md`, `evidence.md`, `state.md`, `deliverables.md`) is created at
> session start, not here.

## Outcome (one sentence)

Beat the confirmed champion `v2-cur2` by at least +0.50 percentage points with a fair,
public-information Bones strategy and a 95% lower bound > 0 on a discovery-tuned selection set, an
independent confirmation set and a 400,000-game final head-to-head — **and** report a measured fair
information ceiling so that a negative result is bounded rather than open-ended.

## Incumbent — this is the bar, not `fair-block`

| Strategy | Path | Verified rate |
|---|---|---|
| **`v2-cur2` (champion)** | `.tmp/fair-block-v2/policies/candidates/v2-cur2.cs` · library `.tmp/fair-block-v2/library` | 30.7670% on 400k (123,068/400,000); 30.7717% pooled over 800k (246,174/800,000) |
| `fair-block` (previous champion) | `.tmp/block-search/library-confirm/.bones/strategy-library/seat-1.json` | 29.5403% on the same 400k; 29.5332% pooled over 800k |
| `c5-newend` (lineage root) | `.tmp/aggression-search/library-fair/.bones/strategy-library/seat-1.json` | 29.0660% on v2's selection seeds |
| chance | — | 25.0000% |

Every arm must be measured against **both** `v2-cur2` and `fair-block`, so the lineage stays legible.

## Why this is a stretch, stated as evidence

1. **The easy lever is gone.** v2 spent its gains: block-model refinements (approaches 1–2) are worth
   ≈+0.14pp alone, and the champion's remaining margin came from a super-additive interaction with a
   one-pass weight correction. There is no known second lever of that size.
2. **Sharper information did not help.** A perfect-information next-player oracle was *worse* than the
   approximate term in the prior campaign (29.132% vs 29.431%), and pass-derived void memory lost
   ~0.14pp at 200k. More information about the block term is demonstrably not the lever.
3. **The exact-count fix is already banked.** Public tile counting is exact (0 mismatches in 292,160
   decisions), so there is no arithmetic headroom left in the block model.
4. **The remaining gap is unknown.** The champion is 5.77pp above chance, but nobody has measured how
   much of that is reachable with public information only. That unknown is the point of this session.

## Definition of done — falsifiable checks

| # | Dimension | Command (harness at `.tmp/fair-block-v3/`) | Expected observable |
|---|---|---|---|
| D0 | Reproduction | `… probe --candidates v2-cur2 --seedBase <discovery start> --seeds 4 --games 2000` | reproduces the v2 rates within seed noise; also re-verify `repro-next80` == `fair-block` bit-exactly |
| D1 | Selection | `… sweep --specs specs/<frozen>.json --seedBase 910635 --seeds 100 --games 2000` | candidate − `v2-cur2` ≥ **+0.50pp** and CI low > 0 |
| D2 | Confirmation | `… sweep … --seedBase 910735 --seeds 100 --games 2000` | CI low > 0 vs `v2-cur2` |
| D3 | Final | `… sweep … --seedBase 910835 --seeds 100 --games 4000` | CI low > 0 vs `v2-cur2` |
| D4 | Persist + reload | `… promote --id <champion>` | `persisted=True`, `matchesExactly=True`, reloaded SHA256 == measured SHA256 |
| D5 | Fairness | `… verify` + token scan | only `state.Hands.GetHand(state.CurrentPlayer)`; no `HandsByPlayer`, no `BonesPlayerId` |
| D6 | Safety | manifest job over `WIP/Wip.Bones.Host/bones-data` | `modifiedAfterSessionStart=0` and identical manifest hash before/after |
| D7 | **Fair ceiling** | `… ceiling` (oracle diagnostic) | a reported oracle-vs-champion gap **with its CI**, on the same field and seeds |
| D8 | **Field transfer** | `… heldout --half A --seeds …` / `--half B` | the champion-vs-`v2-cur2` delta reported separately on both halves |
| D9 | Exhaustion | all six approaches below | one 200,000-game verdict row each in `state.md` |

**Secondary tier (report, do not conflate with D1).** A variant reaching ≥ +0.20pp with CI low > 0 on
*all three* sets is a legitimate but smaller champion claim, reported under its own heading. It is not
"almost +0.5pp" and must never be worded that way.

All claims use `diff ± 1.96·sqrt(p1(1−p1)/n1 + p2(1−p2)/n2)`.

## Hard constraints (gates)

1. **No hidden hands in the champion.** `state.Hands.GetHand(...)` only with `state.CurrentPlayer`.
   Oracle and Monte-Carlo variants are diagnostics; `promote` must hard-refuse any source that fails
   `verify`, including deliberately-oracle sources staged under a separate id.
2. **No opponent-identity modelling.** The field is a fixed list of named historical strategies;
   conditioning on *which* opponent is seated is field exploitation, not skill. Public seat number and
   public pass history are allowed; strategy identity is not.
3. **No writes to `WIP/Wip.Bones.Host/bones-data`.** Everything under `.tmp/fair-block-v3/`.
4. **No production `WIP` source edits; no network calls.**
5. Builds use `-m:1 -nodeReuse:false`; no `dotnet test` (testhost blocked).
6. Do not weaken the field, the game count, or the interval.

## Seed hygiene — nothing ≤ 910534

v2 burned everything through 910534. New blocks, each used for exactly one role:

| Role | Seeds | Games | Purpose |
|---|---|---|---|
| **Discovery** | 910535–910634 | 100 × 2,000 = 200,000 | all tuning, screening, weight search |
| **Selection** | 910635–910734 | 100 × 2,000 = 200,000 | the D1 claim; no tuning on this block |
| **Confirmation** | 910735–910834 | 100 × 2,000 = 200,000 | D2 |
| **Final** | 910835–910934 | 100 × 4,000 = 400,000 | D3 |

**The single biggest methodological repair over v2**: v2 tuned and claimed on the same block. Here the
claim block is virgin. Any arm that is edited after seeing a selection-block number invalidates D1.

## Measurement protocol

- Field: the 64 historical strategies from `.tmp/bench/history.json` excluding `initial-seat-4-v9@00b1a655`,
  plus the 5 deterministic archetypes. Exclude every candidate under test.
- Search uses the **paired** same-deal McNemar interval (≈3.3× sharper; verified in v2: 0.097pp vs
  0.32pp SE at 40,000 games). Claims use the unpaired interval above.
- Screen at 80,000 paired games; promote to a 200,000 unpaired measurement only when the paired CI
  excludes 0 and the effect is locally stable across neighbouring parameter values.
- Never reuse a seed block for two roles.

## The six approaches, in order

### A1 — Residual search with a clean protocol
**Hypothesis.** A second-order search (pairwise interactions, continuous weights, joint block-term
weight) over the champion's ~21 live parameters finds ≥ +0.30pp over `v2-cur2`.
**Why plausible.** v2 ran exactly one pass of one-at-a-time coordinate ascent on 80k paired games and
stopped at a plateau; it never varied two weights jointly and never searched the block weight and the
shedding weights together.
**Falsification.** If a full second pass over the discovery block yields no knob with paired CI
excluding 0, record the champion as locally optimal and close this approach.

### A2 — Multi-ply lookahead under public information
**Hypothesis.** Replacing the linear evaluation with a budgeted planner — enumerate legal moves, sample
opponent hands from the exact unseen pool (correct hand sizes, void constraints), roll the round out
under a cheap fixed policy — beats the linear evaluator by ≥ +0.50pp.
**Why plausible.** This is the only approach that is *qualitatively* different rather than a re-weighting.
**Falsification.** Throughput first: measure games/second on one seed. If a 200k arm cannot finish in
≤ 30 minutes, cap the planner at ≤ 8 samples / ≤ 200 ops per decision and screen at 20,000 games; if the
paired effect at 20k is inside noise, stop. Do **not** silently downgrade the game count for a claim.

### A3 — Go-out and block planner beyond guaranteed silence
**Hypothesis.** v2's silent-chain search only found positions where *both* ends were provably dead and
fired ~19 wins in 200,000. Relaxing to *probabilistic* table silence — maximise P(I empty my hand before
anyone else) at ≤ 4 tiles — finds materially more positions.
**Falsification.** If the relaxed planner's paired effect over 80,000 games is under 0.10pp, the endgame
is not where the wins are; record and move on.

### A4 — Mixed / near-tie policy
**Hypothesis.** The champion is deterministic and resolves score ties by legal-move order; randomising
over near-ties (within δ of the top score, seeded from public state) is worth ≥ +0.10pp or at least
removes an exploitable pattern.
**Why plausible.** v2 only tested last-wins, which scored −0.04pp; the near-tie band was never tested.
**Falsification.** Any δ band that fails to beat the champion on the discovery block is rejected. Mixing
must be a deterministic function of public state (no RNG state across moves) to stay reproducible.

### A5 — Fair information ceiling (D7) — *scheduling gate, not an improvement*
**Hypothesis.** An oracle policy with full visibility establishes an upper bound; the fair-vs-oracle gap
is the honest remaining headroom.
**Why it matters.** It decides how hard to push A2/A3. Run it **before** them.
**Method.** Diagnostic only, never promoted. Use true hands for move scoring and/or shallow search.
Report oracle rate, champion rate, and the difference with a CI.

### A6 — Field transfer on a held-out half (D8) — *validity gate*
**Hypothesis.** The champion's edge over `fair-block` persists on a field half it was not tuned against.
**Method.** Deterministically split the 64 historical strategies into halves A and B; measure champion,
`v2-cur2` and `fair-block` on each at 200,000 games.
**Honesty requirement.** `v2-cur2` was tuned on the full 64, so no half is a clean holdout for *it*.
The **champion-vs-incumbent delta** is still a fair comparison; the absolute rates are not. Say so.

## Round plan (cap 8, one narrow objective per round)

| Round | Objective | Mode | Gate |
|---|---|---|---|
| 1 | Copy and re-verify the harness at `.tmp/fair-block-v3/`; reproduce `v2-cur2` and `fair-block` on discovery seeds; re-run the bit-exact `fair-block` reproduction | Reproduction | D0 |
| 2 | Build and run the oracle ceiling diagnostic; report the fair-vs-oracle gap with CI | Probe | D7 |
| 3 | Held-out-field split A/B measurement of champion vs incumbents | Probe | D8 |
| 4 | A1 residual search on the discovery block (second-order + continuous) | Adversarial | discovery-only |
| 5 | A2 planner feasibility, throughput measurement, then screen | Adversarial | throughput first |
| 6 | A3 relaxed go-out/block planner, and A4 mixed near-tie band | Adversarial | discovery-only |
| 7 | Freeze the best variant; run selection, confirmation, final | Reproduction | D1–D3 |
| 8 | Persist, reload, fairness, safety hashes, exhaustion table, report | Reproduction | D4–D6, D9 |

If the ceiling from round 2 shows the fair gap is under +0.50pp, say so in round 2 and re-plan rounds
4–6 toward the secondary tier rather than spending the budget chasing an unreachable Tier A.

## Stop criteria

- **Champion**: D1–D6 pass.
- **Bounded negative**: D7, D8, D9 all reported, plus the best surviving variant's final 400,000-game CI
  against `v2-cur2`. A negative here is a real result *because* it comes with a measured ceiling.
- **Round cap**: 8 rounds.

## Deliverables

- `docs/goal-bones-fair-block-v3/{goal,journal,evidence,state,deliverables}.md`
- `.tmp/fair-block-v3/` harness (copied from v2, plus `ceiling` and `heldout` commands, plus a
  `promote` that refuses non-fair sources), specs, result JSONs
- Staged champion library under `.tmp/fair-block-v3/library` if Tier A or B is met
- A final report: approaches tested, per-set rates and CIs, ceiling gap, transfer result, champion
  source/path or negative verdict, and live-data manifest hash

## Infrastructure warnings carried over from v2

1. **168 pips, not 147.** The double-six set totals 168 because doubles count twice. A 147 constant
   silently corrupts every blocked-round computation.
2. **The generator is marker-based.** It injects at `BonesMove? bestMove = null;`,
   `if (score > bestScore)` and `private int CountMatchingAfterPlay`, and substitutions must be
   unique or the build must throw. v2 lost a sweep to non-unique patterns and another to a regex that
   matched nothing and silently produced 41 identical arms — assert that every substitution changed the
   text.
3. **Policy sources use LF, not CRLF.** Replacement literals must use `\n`.
4. **``bones-data`` is 366,519 files / 3.2 GB.** Content hashing takes minutes; run the manifest job in
   the background and use `path|size|mtime` ticks.
5. **Dead code in generated sources.** v2's champion shipped two unreachable helpers. Strip them *before*
   the freezing measurement if you care, never after.
6. **`bones-data` write safety is proven by `modifiedAfterSessionStart=0`**, which is stronger than a
   before/after hash pair taken inside the window.

## Explicitly out of scope

- Hidden-hand access of any kind in a promoted strategy.
- Opponent-strategy identity / fingerprinting of the field.
- Editing the benchmark bank, the archetypes, or `history.json`.
- Weakening the field, the game counts, or the confidence interval.
- Promoting anything that has not passed `verify`.