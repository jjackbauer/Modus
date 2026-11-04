# WP-1.3 architecture blocker (2026-11-03)

**Status:** blocked; the working worker route was reverted.

The worker must not reference the host. Adding `Wip.Sandbox.Worker -> Wip.Bones` violated the
`sandbox-worker-no-host` architecture rule:

```text
Unwaived forbidden references: sandbox-worker-no-host: Wip.Sandbox.Worker -> Wip.Bones
```

The route compiled and passed two local tests, but CI architecture correctly rejected it. I reverted
the unsafe route (commits `ef35b5e`, `93179ce`, `24d6899`) to keep the architecture gate green.

## Viable designs

1. **Extract Bones contracts** (`IBonesPlayerSlot`, state/move DTOs) into a contracts project that
   both Bones and the worker reference; the worker still must not reference the host domain/engine.
2. **Assembly-bytes/reflection**: host compiles the model assembly without loading it; worker loads
   it plus the Bones assembly from an explicit path, invokes `ChooseMove` via reflection, and uses
   the existing JSON DTOs. No project reference; host never loads the model assembly.

Either requires an ADR/architecture review; no worker route is claimed.

## Resolution (2026-11-04)

Option 1 was selected (ADR-IMPL-08, option A) and implemented with `Wip.Sandbox.BonesContracts`; the worker references contracts only and `sandbox-worker-no-host` remains enforced. The worker route is now covered by `worker-coverage.md`. This file is kept as history of the blocked state.
