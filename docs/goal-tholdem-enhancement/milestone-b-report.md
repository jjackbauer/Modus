# Milestone B report — a verified four-player evaluation gate

**Date:** 2026-11-10 · **Branch:** `WIP` · **Base commit:** `11eca9e83602b8ae485c815788d1e8b3df271a5e`
**Harness state:** round 3 of 10 · **Status:** Milestone B exit evidence met; D6 and D7 Covered;
D1 and D11 Partial; the full goal (D2–D5, D8–D10, D12) is still open.

## What was delivered

A new, additive evaluation stack in `Wip.Tholdem.Agents.Evaluation`:

1. **Versioned game contract** — `GameConfiguration` with canonical hash, four-seat cash default,
   and explicit refusals for ante, tournament mode, unsupported button/side-pot rules and invalid caps.
2. **Per-hand simulation access** — `MatchSimulator.RunDetailed` returns the unchanged report plus
   per-hand outcomes and hand seeds; an optional per-hand action cap fails closed. The legacy
   446-test suite is intact (450 after the new simulator tests).
3. **Protected seed domains and a pre-registered plan** — `SeedDomain` (training/selection/holdout/audit,
   disjoint concrete seeds, content hash) and `EvaluationPlanV2` (game, pool, compositions, hands,
   margin, confidence, planned candidates, anchor tolerance, plan hash).
4. **Frozen opponent population** — five behaviors (tight-passive, tight-aggressive, loose-aggressive,
   calling-station, adversarial-raiser), four compositions, disjoint holdout identities and a pinned
   pool hash.
5. **Block statistics** — normal + deterministic bootstrap bounds at the seed-block unit, conservative
   min/max, AS241 inverse-normal quantile.
6. **Paired focal-seat evaluator** — replaces exactly one seat at a time against three frozen
   opponents on identical deals, rotates the focal seat through all four positions, asserts paired
   hand seeds, reports bb/100 units, per-composition/per-seat estimates and tail risk.
7. **Statistical v2 gate** — recomputes every bound from `Units` (never trusts the stored estimate),
   applies Bonferroni-adjusted confidence, enforces anchor non-regression, and returns the taxonomy
   `verified-improvement / rejected / inconclusive / unsafe / unverified`.
8. **Independent verifier** — re-runs both evaluations with fresh agents, requires report-hash
   equality, then requires the supplied decision to equal the gate decision.
9. **Control experiment** — pre-registered holdout (288 blocks, four compositions), a known
   dominant/dominated pair, a cold byte-identical rerun, and refused forced/tampered controls.

## Verified evidence

| Fact | Evidence |
|---|---|
| Baseline suite is 446/446 before any change | EVD-007, EVD-008 |
| Legacy two-seat bytes are unchanged by `RunDetailed` | EVD-010, EVD-011 |
| Domain/simulator contract is hashed and refuses unsupported settings | EVD-012 |
| Seed derivation matches an independent 64-bit recomputation | EVD-013 |
| Foundation suite is 468/468 after the contract layer | EVD-014 |
| Five archetypes show distinct measured action profiles; pool hash pinned | EVD-015 |
| Evaluator pairs seats/deals; self-comparison is exactly 0.0 | EVD-016 |
| Dominated control on 96 blocks: mean +97.51, LCB +57.96 | EVD-017 |
| Gate ignores a poisoned stored estimate and applies Bonferroni | EVD-018 |
| Verifier refuses forced promotion and tampered reports | EVD-019 |
| Pre-registered holdout: mean +199.74, LCB +176.92, anchor LCB +115.04 | EVD-020 |
| A separate process reproduces all six artifacts byte-for-byte | EVD-021 |
| Report file SHA-256 equals the claimed `ReportHash` | EVD-022 |
| Full suite after all changes is 495/495 | EVD-019, EVD-023 |

## Milestone B exit evidence

- (a) **Policy vs itself is exactly zero.** `Evaluate_GivenPolicyVersusItself_ReportsExactlyZero`
  asserts 0.0 for the mean, both bounds, every unit and both net-chip fields.
- (b) **A known stronger scripted agent beats a dominated one on pre-registered four-seat data.**
  The fold-only challenger beats call-any with mean +199.738 bb/100 and lower bound +176.917 over
  288 held-out blocks against four frozen compositions; the anchor population is non-regressing.
- (c) **A forced-false promotion is refused.** The verifier returns `decision-mismatch` for a forced
  "verified improvement" and `report-mismatch` for a shifted report.
- (d) **The report is reproducible.** The verifier recomputes both reports with fresh agents inside
  the run; a second process wrote byte-identical artifacts for all six files.

No LLM was involved. No champion pointer moved.

## Coverage

| Dimension | Status | Note |
|---|---|---|
| D1 four-seat simulation contract | Partial | Contract + `RunDetailed` done; the CLI/API still defaults to heads-up (SIM-001 acceptance not met) |
| D2 observation truthfulness | None | not started |
| D3 equity separation / multiway equity | None | not started |
| D4 policy semantics | None | not started |
| D5 causal diagnostics | None | not started |
| D6 paired evaluator + gate | **Covered** | EVA-001..011 with the control experiment |
| D7 frozen opponent population | **Covered** | OPP-001..003 |
| D8 semantic no-op gate | None | not started |
| D9 LLM attribution / ablation | None | not started |
| D10 meta-learning | None | not started |
| D11 reproducibility/safety | Partial | report hashes, disjoint domains and v1 preservation verified; ART-005/006 deny-test and RUN-001 budgets not |
| D12 operator visibility | None | not started |

## Reproduction

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- \
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
dotnet run --project docs/goal-tholdem-enhancement/experiments/control-v1/harness -c Release -- \
  docs/goal-tholdem-enhancement/experiments/control-v1/results
```

The first two produce `TOTAL 495 PASSED 495 FAILED 0 SKIPPED 0`; the third prints
`decision VerifiedImprovement verified-improvement`, `verifier True verifier:verified`, and both
negative controls refused, then exits 0.

## What is not claimed

- No claim that the fold-only policy is a good poker strategy or that a champion was improved.
- No claim that the synthetic four-seat population represents real opponents.
- No LLM, meta-learning or search-ablation claim; those milestones have no evidence yet.
- No CI result: local verification uses the compiled-assembly runner because the VSTest testhost
  aborts on this machine (`Win32Exception 5`).

## Next objective

Wire the v2 evaluator/gate into a four-seat CLI/API surface without changing the v1 commands
(D1 / SIM-001), then start Milestone C with the equity type split and N-opponent expected pot share
(D3 / EQ-001..005).
