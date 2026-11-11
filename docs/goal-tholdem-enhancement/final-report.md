# Final campaign report — WiP four-player Hold'em enhancement benchmark

**Date:** 2026-11-10 · **Base commit:** `11eca9e83602b8ae485c815788d1e8b3df271a5e` (branch `WIP`)
**Campaign:** 12 goal rounds / 15 harness rounds plus one control and two policy experiments.
**Status:** **partial delivery; the goal is not complete and remains active.**

## 1. Verdict against the delivery objective

| Anchor | Verdict |
|---|---|
| Pre-registered, four-player, seat-rotated, held-out evaluation | **Delivered.** A persisted plan drives a paired focal-seat evaluator over balanced seat rotations against frozen compositions, with block-level confidence and an independent verifier. The control experiment reproduced a positive result and a byte-identical rerun. |
| Clears regression guards against diverse, unseen opponents | **Delivered.** The gate refuses anchor-pool regressions; it rejected three of four unsafe random-search results and the equity-policy experiment (`unsafe-anchor-regression`). |
| Outperforms equal-budget, non-LLM controls over multiple tasks | **Tested and not achieved (null).** Greedy verified zero tasks; Random verified one of four while unsafe on three. No learning-competence claim is made. |

Release claims R1–R4 are assessed in §5. In short: **R1 is substantially delivered**, **R2 and R4 are not demonstrated**, **R3 was not tested**.

## 2. Verified stack

### Four-seat evaluation
- `GameConfiguration` (versioned, hashed, refuses unsupported ante/tournament/sizing semantics).
- `MatchSimulator.RunDetailed` (report + outcomes + hand seeds; additive, legacy bytes preserved).
- `EvaluationPlanV2` + strict `EvaluationPlanV2Json` round-trip; protected training/selection/holdout/audit domains.
- `FrozenOpponentPool` (five archetypes, four compositions, disjoint holdout identities, pinned hash).
- `PopulationMatchEvaluator` (exactly one focal seat, paired decks, 4 or 24 seat assignments, bb/100 units).
- `BlockStatistics` (normal + deterministic bootstrap, conservative bounds).
- `PopulationPromotionGate` (recomputes from `Units`, Bonferroni-adjusted, anchor non-regression, verdict taxonomy).
- `PopulationPromotionVerifier` (fresh-agent recomputation, report/decision mismatch refusal).
- `evaluate4` CLI (persisted plan → canonical report → optional gate/verification; no pointer writes).

### Equity, policy and observations
- Typed `EquityEstimate` vs `HandCategoryScore` (no conversion).
- N-opponent Monte Carlo expected pot share, `1/k` ties, no replacement (exhaustive river delta 0.0017; four-way board tie exactly 0.25).
- Weighted 169-class opponent ranges and range-conditioned sampling (KK vs AA-only share 0.182 vs uniform 0.825).
- Confidence-gated call/fold/uncertain decisions; contribution-aware side-pot call EV (layers 150/100, break-even 0.32 in the fixture).
- `EquityPolicyAgent` using multiway equity, preflop situation classification and pot-relative sizing; legal and deterministic, but passive in its probe.
- `PlayerObservation` exposes total commitments and normalized action history without changing canonical bytes; measured VPIP with nullability and denominators (0/10 is 0.0, 0/0 is null).

## 3. Milestone B evidence (the positive result)

Pre-registered control experiment, 288 holdout blocks × four compositions × 20 hands:
- Primary: fold-only vs call-any, mean **+199.738 bb/100**, 95% lower bound **+176.917**.
- Anchor: mean +144.651, lower +115.038 (no regression).
- Gate: `VerifiedImprovement`; verifier `verifier:verified`; forced promotion and a shifted report both refused.
- A second process produced **byte-identical** reports, decision and verification.
- Report hash `40201148…`; anchor hash `f4035674…`.

## 4. Negative results (kept, not reframed)

| Experiment | Result |
|---|---|
| Equal-budget proposer ablation (4 tasks, 4 arms, 8 candidates, 737,280 focal hands) | **no-benefit**: NoSearch 0 verified, Random 1 verified + 3 unsafe, Grid 0 + 3 unsafe, Greedy 0 + 1 unsafe. The corrected outcome rule requires the proposal arm to have ≥2 verified tasks, zero unsafe, and strictly beat every control. |
| Equity-policy vs legacy promotion (pre-registered) | **Falsified HYP-002**: primary mean **−93.83 bb/100**, lower −454.76; anchor lower −961.73; gate `Unsafe / unsafe-anchor-regression`; verifier true. Report hash `4c18e8bc…`. |

The first run of the equal-budget ablation mechanically returned `benefit`; that classification was rejected as not credible and the rule was tightened before acceptance. The equity policy is passive (19 of 22 decisions fold/check in its probe); this configuration is worse, not evidence that equity-aware policies cannot work.

## 5. Release claims

| Claim | Status |
|---|---|
| **R1 — correct four-player benchmark** | **Substantially delivered.** Reproducible, independently verified evaluator, multiway equity, opponent pool and statistical promotion. Gaps: `train` still heads-up (D1), no versioned `DecisionContext` or per-seat opponent profiles (D2), legacy agent still uses category-vs-pot-odds (D3). |
| **R2 — useful strategy search** | **Not demonstrated.** The equal-budget ablation was a null; the equity policy experiment was negative. |
| **R3 — LLM value demonstrated** | **Not tested.** No model calls were made in this campaign; no LLM claim. |
| **R4 — harness learning demonstrated** | **Not demonstrated.** Null result; no recursive self-improvement claim. |

## 6. Coverage

| Dimension | Status | Remaining P0 rows |
|---|---|---|
| D1 four-seat contract | Partial | `train` four-seat default (SIM-001 CLI acceptance) |
| D2 observations | Partial | versioned DecisionContext, per-seat opponent profiles, board texture/VPIP suite (OBS-001/006/007/008) |
| D3 equity | Partial | legacy agent equity replacement, range inference from history, per-layer equities, side-pot integration |
| D4 policy | Partial | full preflop situations, action traces, exploitative deviations, skill composition |
| D5 diagnostics | None | DIA-001..010 |
| D6 paired evaluator/gate | **Covered** | — |
| D7 opponent population | **Covered** | — |
| D8 mutation/search | Partial | mutation gates, effective identity, novelty/lineage |
| D9 LLM attribution | None | LLM-001..013 |
| D10 meta-learning | None | META-001..009 |
| D11 reproducibility/safety | Partial | deny-tests, budgets, atomic recovery, retention |
| D12 operator UX | None | UX-001..005 |

## 7. Verification and reproduction

- Full Tholdem suite: **570/570**, 0 failed, 0 skipped (last run after round 14; docs-only changes since).
- Solution build: 0 errors; `HarnessSync --check`: exit 0.
- `dotnet test` is unusable on this machine (VSTest testhost `Win32Exception 5`); the compiled-assembly runner is the local evidence path, CI remains authoritative.
- Control experiment: `dotnet run --project docs/goal-tholdem-enhancement/experiments/control-v1/harness -c Release -- docs/goal-tholdem-enhancement/experiments/control-v1/results` (exit 0, verifier accepts, controls refused).
- Equity experiment: see `experiments/equity-policy-v1/README.md` (gate Unsafe, verifier true).
- Learning ablation: see `experiments/learning-ablation-v1/README.md` (`no-benefit`).

## 8. Honest limits

- No LLM, meta-learning, diagnostics or UX work with evidence; those dimensions are None.
- The equity-policy artifact v1 omits `PreflopRaiseMargin` (fail-closed; default-only).
- Observation inference from public action history and per-layer equity are not wired.
- The four-seat CLI covers evaluation but not four-seat training/promotion.
- The positive control is a fold-only vs call-any policy pair; it demonstrates the instrument, not poker strength.

## 9. State of the goal

The goal is **active and not complete**. The 12 goal rounds are exhausted; a future campaign should
start with: (1) a four-seat training/promotion path, (2) diagnosis-driven, richer-policy proposer for a
credible anchor-3 retry, (3) D5 diagnostics, (4) LLM attribution and equal-budget ablation only if a
model budget is explicitly provided.
