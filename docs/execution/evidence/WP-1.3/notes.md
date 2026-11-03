# WP-1.3 — trusted worker execution for Bones

**Status:** routing implemented; full-suite verification pending.

## Delivered

- Worker protocol messages `compileBones` and `chooseBonesMove`, with `BonesWorkerState`/`BonesWorkerMove` DTOs.
- Worker compiles model strategies against `Wip.Bones`, loads them in a collectible ALC, and executes `IBonesPlayerSlot`.
- `BonesStrategyScriptHost` Worker mode sends source/state to the worker and uses a `WorkerBonesPlayerSlot`; it does **not** load the model assembly in the host.
- Worker mode still requires `TrustedOnly=true` and an installed `SandboxWorkerClient`; otherwise it fails closed.
- Test `BonesStrategyScriptHost_GivenWorkerMode_ExpectedExecutionThroughWorker` passes.

## Limits / residual

- The DTO omits the Bones event log, outcome, and winner; strategies that depend on those fields do not get them yet.
- Worker mode is trusted-only; Windows OS containment residual remains accepted; no release.
- The full Bones suite must be regenerated at 386 tests; CI/waivers are being updated.
