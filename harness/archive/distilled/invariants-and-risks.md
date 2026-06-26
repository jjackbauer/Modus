# Invariants And Risks

> **Scope**: Architectural invariants, operational risks, and mitigation strategies.
> **Confidence**: Low
> **Last updated**: 2026-05-16
> **Source entries**: OBS-000

- ❓ Invariant: host is the only composition root.
- ❓ Invariant: plugins depend on contracts, not host internals.
- ❓ Risk: plugin side effects during registration can cause startup instability.
- ❓ Risk: contract drift can break plugin compatibility.
