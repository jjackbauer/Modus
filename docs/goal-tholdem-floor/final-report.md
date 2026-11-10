# Final report — positive-evidence signal floor

> Goal: evaluate a positive-evidence signal floor in Tholdem's signature-driven refinement policy with a pre-registered DEV tuning pass and one fresh holdout, adopt it only if it preserves exploration while removing arbitrary zero-rate edits, and otherwise retain the shipped floor-0 policy with the rejection recorded.

## Outcome

**Evaluated and rejected as behaviorally inert; the shipped floor 0 is retained.**

The floor was implemented as a default-0 product option and verified at policy and loop level. On the six DEV seeds and six never-used holdout seeds, every positive floor produced **identical challengers, divergence and means to floor 0**. The pre-registered success rule still passes for the 0.05 arm versus baseline, but that gain belongs to the existing signature arm, not to the floor. Because the floor removed no observed edit, the no-op adoption rule keeps **0**.

## Requirements

| # | Dimension | Status | Evidence |
|---|---|---|---|
| F1 | Floor option | Covered | EVD-002, EVD-003, EVD-004, EVD-005 |
| F2 | Pre-registration | Covered | `goal.md`, `round-1/experiment-design.md`; fixed before any floor result |
| F3 | DEV tuning | Covered | EVD-006, EVD-007, EVD-008 |
| F4 | Fresh holdout | Covered | EVD-010, EVD-011, EVD-012 |
| F5 | Decision and report | Covered | EVD-012, EVD-013; this report |

## F1 — product change

- `SignatureDrivenRefinementPolicy` gained a last-optional `minimumSignalRate = 0.0` (finite `[0,1]`, `MinimumSignalRate` property). The filter is inclusive: `Sample >= 10 && Rate >= floor`. No passing signal falls back to the unchanged legacy proposer.
- `TrainingOptions.SignalRateFloor = 0.0` is passed at the single policy construction site in `TrainingLoop`.
- 7 new tests; default 3-iteration challengers unchanged (`d24dcf6d` / `c34333fe` / `86f42856`, `Bootstrap == null`); floor 1.0 real run equals legacy `0ceaa8fd`; full suite **394/394**; legacy CLI **7/7**; frozen snapshot unchanged.

## F3 — DEV floors

| Arm | Median divergence | Zero-effect | Promotions |
|---|---:|---:|---:|
| Baseline | 0.0083 | 7/30 | 0 |
| Floor 0 | 0.0875 | 0/30 | 0 |
| Floor 0.05 | 0.0875 | 0/30 | 0 |
| Floor 0.10 | 0.0875 | 0/30 | 0 |
| Floor 0.20 | 0.0875 | 0/30 | 0 |

The three positive floor files are byte-identical to each other and field-identical to floor 0 on all 30 refinement rows. The mechanical selection rule chose 0.05 because every positive floor is non-inferior, but no zero-rate edit was observed to be filtered.

## F4 — fresh holdout

Six never-used seeds (`1099511827776 + i*1000`), one attempt, three arms on the same seeds:

| Metric | Baseline | Floor 0 | Chosen 0.05 |
|---|---:|---:|---:|
| Median divergence | 0.0167 | 0.0875 | 0.0875 |
| Zero-effect | 7/30 | 0/30 | 0/30 |
| Promotions | 0/30 | 0/30 | 0/30 |

- Chosen vs baseline: median per-seed ratio **4.906** (pooled 4.8125; one zero-baseline substitution), bootstrap 95% CI **[3.917, 16]**; zero-effect 7/30 → 0/30. Pre-registered success rule met.
- Chosen vs floor 0: ratio **1.0** on every seed, equal zero-effect. The 30 chosen rows are field-identical to the 30 floor-0 rows.
- **Adoption rule:** because the floor is a no-op relative to floor 0, retain 0; the success-vs-baseline result does not justify changing the policy.

## Decision

**Keep `SignalRateFloor = 0`.** Do not promote 0.05 (or 0.10/0.20) as a recommended or default setting. Keep the default-0 option and its tests in the product; a future adversarial fixture can force a zero-rate signal and prove the rescue end-to-end.

## Residual limits

- No tested seed actually selected a zero-rate signal, so the end-to-end zero-rate rescue path was not observed; the filter is unit-tested, and the option remains default-off.
- The experiment measures decision divergence against one fixed baseline, not poker strength; no promotions occurred in any arm.
- `dotnet test` is unusable locally (`Win32Exception (5)`); the supplementary runner is the evidence, and no CI-green claim is made.
- No release, publish, merge or deploy; the tree remains uncommitted.

## Files

- [goal.md](docs/goal-tholdem-floor/goal.md) — contract and pre-registration.
- [round-1/experiment-design.md](docs/goal-tholdem-floor/round-1/experiment-design.md) — floors, seeds, selection and success rules.
- [round-2/floor-selection.md](docs/goal-tholdem-floor/round-2/floor-selection.md) / `.json` — DEV selection.
- [round-3/holdout-summary.md](docs/goal-tholdem-floor/round-3/holdout-summary.md) / `.json` — fresh holdout.
- [journal.md](docs/goal-tholdem-floor/journal.md), [evidence.md](docs/goal-tholdem-floor/evidence.md), [state.md](docs/goal-tholdem-floor/state.md).

*All claims trace to raw rows; the inert result is reported, not rounded into an adoption.*
