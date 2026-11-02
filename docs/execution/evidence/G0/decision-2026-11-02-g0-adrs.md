# G0 decision record — ADRs and CI push

**Date:** 2026-11-02T12:51-03:00
**Decision owner:** product owner (user)

## Decisions

1. **CI push:** authorized to push **`WIP`** so the new `Tests CI` workflow runs. No force-push was authorized or used.
2. **ADRs:** accepted **ADR-IMPL-01 through ADR-IMPL-07** as written, with:
   - ADR-07 expiring **2026-11-30** (WP-2.6 removes the ShellHost→Bones coupling);
   - ADR-02/03/04/05 still requiring G2/G3/G4 for their concrete mechanisms/contracts;
   - DEV-002 (project-level CI categories instead of xUnit `Category` traits until WP-4.3) noted.

## Effect

- WP-0.6 moves to **done**.
- G0 remains **NOT READY** until the pushed CI run passes the waiver/zero-test gate and produces run evidence.
- Phase 1 must not start before that.
