# WP-6.1 stabilization evidence (2026-11-05)

**Status:** partial. Regression/flake work and process-leak checks are done; the 24-hour soak and the
2,000-game benchmark have **not** been executed and remain G5 blockers.

## CI green runs on successive commits

| HEAD | Tests CI | Modus CI |
|---|---|---|
| `89308d1` | `42048062612` all six jobs success (Runtime 137) | `42048062647` success |
| `6453c38` | Tests run superseded/cancelled by later pushes | `42056809397` success with the blocking legacy suite |
| `9dc8a56` | `42059745946` all six jobs success (Runtime 146 / Shell 34) | `42059745863` success |

Three distinct code integration points have green authoritative gates (the latest is fully green on
both workflows). Cancelled runs are recorded as superseded, not counted as green.

## Flake fixes landed this run

- Modus migration cadence gate: reference-timer rate parity (12/12 loaded runs; slow/fast/dead
  controls fail).
- `BonesLoopStateConcurrencyTests`: bounded writer/reader coordination (15/15 on one core; violation
  controls fail).
- Legacy timer loop wait widened to a bounded 10 s; Makefile targets restored.
- DeepSeek streaming/retention tests: controls confirm they fail under stubs.

## Leak and resource checks

- Host E2E asserts the worker-mode log line appears once and **no worker process remains** after
  host exit.
- Worker memory bounded after the WP-1.3 fix (60.7 MB peak over 16 compiles vs 328.5 MB before).
- Sandbox hostile/protocol suite 18/18 on Windows and Ubuntu CI.

## Not executed (G5 blockers)

- 24-hour Bones stub soak.
- 2,000-game benchmark / formal SLO regression run.
- A formal release-freeze window with three consecutive full gate runs after the freeze commit
  (only successive integration points are green so far).
