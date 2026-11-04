# WP-1.3 — Worker routing for trusted strategies

**Status:** done 2026-11-04. Worker is trusted-only and default-off (DEV-004); Windows C1 residual accepted; no release.

## Delivered

- `Wip.Sandbox.BonesContracts` (41st WIP project): `IBonesPlayerSlot`, round state, moves, identifiers and domain value types; namespaces unchanged.
- `Wip.Bones` references contracts; the worker references contracts, **not** `Wip.Bones`; `sandbox-worker-no-host` remains intact (architecture 3/3, graph 41).
- Worker `compileBones`/`chooseBonesMove`; host `WorkerBonesPlayerSlot`; host Worker mode requires `TrustedOnly=true` and a worker client or a resolvable `ScriptHost:WorkerPath`.
- Worker memory is bounded: metadata references are created once per process and a forced full collection runs after each compile (measured peak 60.7 MB over 16 compiles, was 328.5 MB and OOM at compile 7 under the 256 MiB Job Object cap).

## Verification

- 9 new worker-mode test cases: state round-trip theory (board/hands/current player/pass streak/legal moves), public player-slot legality fallback with the warning artifact, host-never-loads proof plus InProcess positive control, compile-cache reuse, and worker-side compile rejection.
- Full Bones suite **395** total (393 passed, 2 waived); architecture **3/3** with 41 projects; sandbox worker suite **18/18**; 16-project offline gate green.
- Details and measured numbers: [worker-coverage.md](worker-coverage.md). Architecture history: [architecture-blocker.md](architecture-blocker.md).

## Remaining

- WP-1.6 flag/documentation (see `docs/execution/evidence/WP-1.6/notes.md`).
- Full G2 re-review after 2026-12-03; Windows containment not claimed; release blocked on G5.