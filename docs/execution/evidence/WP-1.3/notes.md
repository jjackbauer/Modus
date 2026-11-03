# WP-1.3 — Option A contracts extraction (trusted worker)

**Status:** implementation working; broader coverage pending.

## Delivered

- New `Wip.Sandbox.BonesContracts` project (41st WIP project) containing the player-slot, round state, moves, identifiers, and domain value types. Namespaces are unchanged.
- `Wip.Bones` references contracts; the worker references contracts, **not** `Wip.Bones`; `sandbox-worker-no-host` remains intact.
- Worker `compileBones`/`chooseBonesMove`; host `WorkerBonesPlayerSlot`; host Worker mode requires `TrustedOnly=true` and a `SandboxWorkerClient`.
- Test `BonesStrategyScriptHost_GivenWorkerMode_ExpectedExecutionThroughWorker` passes.
- Full Bones suite **386** total (384 passed, 2 waived); architecture **3/3** with 41 projects; 16-project gate green locally.

## Remaining

- Only one worker-mode test; broader routing/coverage is pending.
- WP-1.6 flag/docs; G1/Phases 2-6; full G2/release. Windows C1 residual accepted.
