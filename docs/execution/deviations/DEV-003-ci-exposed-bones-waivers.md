# DEV-003 — Six CI-exposed Bones failures waived

**Status:** proposed for G0 review
**Date:** 2026-11-02
**Owner:** Bones engineer + QA
**Expiry:** 2026-11-16

## What changed

Two CI runs exposed six deterministic failures that the local red baseline did not record:

- `Wip.Bones.Tests` — the board-orientation behavior-proof meta-gate, which also executes mapped Web tests.
- `Wip.Bones.Host.Tests` — four host behavior tests and one status-API behavior-proof meta-test.

The exact project/test pairs are in `docs/plan/test-waivers.json` with source `CI 41486872592`.

## Why waiver rather than fix now

- The Bones board meta-gate depends on a nested Web/Playwright matrix; the optional Web lane is not part of Phase 0 and needs its own browser-cache/isolation work.
- The host failures are ordering/resource-sensitive on the 2-core CI runner; the local red baseline had this suite aborted, so these exact tests were never observed.
- Both classes are already owned by WP-2.1 (Bones host/loop hardening), which is the correct fix point.

## Impact

- The offline gate remains truthful: these tests still run and fail, are counted in the 15-project inventory, and are not skipped or deleted.
- The gate becomes green only because the exact failures are recorded with owners and expiry.
- The G0 packet discloses the six new waivers; G0 remains a human decision.

## Reversal

Fix the nested Web matrix and host ordering in WP-2.1, then delete these six entries from `test-waivers.json`. A new failure with any other name still fails the gate.
