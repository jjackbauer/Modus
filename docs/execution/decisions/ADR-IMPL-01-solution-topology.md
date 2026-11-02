# ADR-IMPL-01 — Solution topology

- **Status:** Proposed (acceptance requested at G0)
- **Date:** 2026-11-02
- **Owner:** tech lead
- **Reversal cost:** Medium

## Context

At the baseline, `Wip.slnx` contained only 24 of 37 WIP projects; Bones, the benchmark, recovery, and `Wip.Shell.E2E` were outside the WIP gate. Modus has its own product solution. The plan requires one honest WIP build graph without forcing Modus consumers to build Bones.

## Decision

- `Wip.slnx` is the **full WIP family release gate**: all 37 WIP projects plus the WIP architecture test project when it lands (WP-0.5).
- `Modus.slnx` remains the **Modus product solution**; it may include WIP core projects but is not the WIP release gate.
- A project may appear in both solutions; it may not appear in neither.
- `tools/verify-solution-coverage.ps1` fails when a `WIP/**/*.csproj` is absent from `Wip.slnx`.

## Consequences

- Positive: one build/test gate covers the flagship Bones app; orphans are visible.
- Negative: `Wip.slnx` builds web apps and test projects, so it is slower than a core-only solution.
- Modus consumers can still build a smaller graph.

## Reversal triggers

- Modus consumers cannot accept the WIP project count or build time.
- CI cannot build the 37-project graph on the supported runners.
- A product decision splits Bones into a separate repository.

## Open questions

- Should the WIP architecture tests live under `tests/` or `WIP/`? Owner: tech lead.
- Should `Wip.slnx` include only Release configurations? Owner: CI owner.
