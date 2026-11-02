# ADR-IMPL-07 — Temporary ShellHost-to-Bones coupling waiver

- **Status:** Accepted at G0 (2026-11-02); expires 2026-11-30 / WP-2.6
- **Date:** 2026-11-02
- **Owner:** tech lead
- **Reversal cost:** Low

## Context

WP-0.5 requires `Wip.ShellHost` to have no reference to `Wip.Bones.*`. The current working tree still has a direct `ProjectReference` from `Wip.ShellHost` to `Wip.Bones.Agents` (and therefore a transitive reference to `Wip.Bones`). WP-2.6 removes this coupling by moving the E2E registration into a test-only host or plugin.

The architecture test must fail on new forbidden references today, while allowing this known pre-existing edge until WP-2.6 lands.

## Decision

- Allow **only** `Wip.ShellHost -> Wip.Bones.Agents` and its transitive `Wip.ShellHost -> Wip.Bones` edge.
- Record both in `tests/Wip.Architecture.Tests/architecture-waivers.json` with owner `tech lead`, expiry **2026-11-30**, and this ADR.
- The architecture test fails if either waiver is stale (the edge is gone but the waiver remains), forcing cleanup when WP-2.6 lands.

## Consequences

- Positive: WP-0.5 can enforce all other rules now; the known coupling is visible and time-bounded.
- Negative: the shell host can still load Bones until WP-2.6; this is a release blocker if it remains at G5.

## Reversal triggers

- WP-2.6 removes the reference: delete the waivers and mark this ADR Superseded.
- The coupling grows beyond `Wip.Bones.Agents`/`Wip.Bones`: do not extend the waiver; treat it as a new violation.

## Open questions

- None; WP-2.6 owns removal.
