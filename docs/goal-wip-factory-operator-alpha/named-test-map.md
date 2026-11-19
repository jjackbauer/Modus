# Named-test map — measured at the v0.4 remediation revision

This file replaces the stale round-29 narrative. The authoritative machine-readable inventory is
`named-test-coverage.json`; it is regenerated from compiled assemblies and source assertions by
`TraceabilityTests.Traceability_GivenMeasuredRepository_PublishesSeparateCounts`.

| Class | Count |
|---|---:|
| Named in v0.3 §12.2–12.3 | 80 |
| `exact-and-executable` | 32 |
| `missing behavior` | 48 |
| `name-only-no-assertion` (fails the gate) | 0 |

## What changed since the previous map

The previous record said **15 of 80** (later corrected to 26, then 31 in the delivery narrative). Those figures were
snapshots of different revisions and counting methods. This document does not reconcile them by choosing one; the
current number is **32 exact** at the final measured revision, obtained by reflection over the four built test
assemblies and a per-method assertion probe. Prior numbers remain historical claims, not current measurements.

The 48 missing names are **not** evidence that the behavior is absent in every case: some mechanisms now exist under
different tests (for example the v0.4 review suite proves four independent checks, and
`Template_GivenPolicyContractApproval_CannotAuthorizeMerge` proves D-09 cannot authorize a merge under a v0.4 name).
But the v0.3 names are still absent, so the traceability gate remains open. Writing pass-through tests to raise the
number is forbidden by the remediation requirements and was not done.

## Reproduce

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/goal-wip-factory-operator-alpha/run-factory-gates.ps1
```

The gate report publishes `passedCases`, `failedCases` and `namedCoverage` separately; it exits non-zero on a failed
suite or a name-only placeholder. Missing named behaviour is published, not hidden.
