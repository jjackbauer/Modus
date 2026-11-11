# Equity-policy vs legacy experiment v1 — negative result

**Question:** does the default equity-aware policy beat the legacy four-parameter strategy on the
pre-registered holdout population without regressing on the anchor composition?

**Answer observed:** **no.** The challenger lost by a large margin and the gate returned
`Unsafe / unsafe-anchor-regression`. HYP-002 is falsified.

## Pre-registration

`preregistration.json` was written before the evaluation; hash
`67e048bcc39709c3c0cffe90a077f043cdcf784a355b84584427a4b43ee0c6d1`.

- Plan: holdout pool `tholdem.opponents.holdout.v1`; compositions `primary-mixed`, `three-tag`;
  16 holdout blocks; 2 hands per seat match; margin 10 bb/100; confidence 0.95; 1 planned candidate;
  anchor tolerance 5 bb/100. Plan hash `3af4d905…`; anchor plan hash `b67d7cad…`.
- Challenger: `equity-default-v1` (`EquityPolicy` defaults), policy hash `760ac54c…`.
- Incumbent: legacy strategy `tholdem-strategy-v1-f100e793`, hash `f100e793…`.
- Pools and plans are frozen; no selection or training run touched this holdout.

## Observed result

| Metric | Value |
|---|---|
| Primary mean | **-93.828 bb/100** |
| Primary reported lower bound | **-454.757 bb/100** |
| Units | 32 (16 blocks x 2 compositions) |
| Gate verdict | **Unsafe / unsafe-anchor-regression** |
| Anchor lower bound | **-961.734 bb/100** |
| Verifier | **verified / verifier:verified** |
| Report hash | `4c18e8bc1dff4855021b9422277069253ef4b8fad988fe3da7eec0db09ae4f9d` |

The report file's SHA-256 equals the claimed `ReportHash`. No champion pointer was written.

## Interpretation

The default equity-aware policy is passive (round 9 observed 19 of 22 decisions as fold/check) and
loses to the legacy heuristic in four-way play; it also collapses on the anchor composition. This is a
real negative result for this policy configuration, not a harness failure: the verifier recomputed the
reports and confirmed the gate decision. It is **not** evidence that equity-aware policies are
impossible; it is evidence that this passive configuration is worse, on this pre-registered sample.

## Reproduction

```
dotnet run --project docs/goal-tholdem-enhancement/experiments/equity-policy-v1/harness -c Release -- \
  docs/goal-tholdem-enhancement/experiments/equity-policy-v1
dotnet run --project WIP/Wip.Tholdem.Host -c Release -- evaluate4 \
  --plan docs/goal-tholdem-enhancement/experiments/equity-policy-v1/plan.json \
  --challenger equity-default-v1 --challenger-kind equity-policy \
  --incumbent tholdem-strategy-v1-f100e793 --incumbent-kind strategy \
  --anchor-plan docs/goal-tholdem-enhancement/experiments/equity-policy-v1/anchor-plan.json \
  --data-dir docs/goal-tholdem-enhancement/experiments/equity-policy-v1/data \
  --out docs/goal-tholdem-enhancement/experiments/equity-policy-v1/results
```
