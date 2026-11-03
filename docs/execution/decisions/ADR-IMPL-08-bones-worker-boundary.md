# ADR-IMPL-08 (proposed) — Bones worker boundary

**Status:** accepted — option A (contracts extraction) selected by the user. WP-1.3 is blocked by the
`sandbox-worker-no-host` rule: `Wip.Sandbox.Worker` must not reference `Wip.Bones`.

## Options

### A. Extract Bones contracts

Move the player-slot contract and the state/move types needed by strategies into a
contracts project referenced by both Bones and the worker. The worker would compile
against contracts only; the host domain/engine stays out of the worker graph.

- Pro: worker owns compilation/execution; strongest alignment with the threat model.
- Con: the strategy API uses `BonesRoundState`, `BonesBoard`, `BonesHands`, `BonesMove`,
  and identifiers; extracting them is a large, breaking domain refactor and can blur the
  host/contract boundary.

### B. Assembly-bytes/reflection

The host compiles the model strategy to bytes without loading it and sends the bytes plus
the existing JSON state/move DTOs to the worker. The worker loads the model assembly and
the Bones assembly from an explicit path in its ALC, invokes `ChooseMove` via reflection,
and never references Bones at build time.

- Pro: no worker→host project edge; host never loads model code; smaller change.
- Con: compilation happens in the host (Roslyn only, no execution); reflection replaces
  the typed slot; the host must expose the Bones assembly path; needs an OS-containment
  review.

## Recommendation

B is the smaller safe step and preserves the architecture rule. A is cleaner long-term
but is a large domain refactor. Do not relax `sandbox-worker-no-host`.

No implementation until approved.
