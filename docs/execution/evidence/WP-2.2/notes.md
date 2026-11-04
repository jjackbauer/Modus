# WP-2.2 — Remove `StrategyPool.Instance`

**Status:** implementation complete 2026-11-04; gate verification pending.

## Delivered

- `StrategyPool` no longer has a process-wide `Instance` property and its constructor no longer assigns one. The pool is a host DI singleton, nothing else.
- Host composition (`BonesHostServiceCollectionExtensions`) drops the static unification block and both `StrategyPool.Instance = ...` assignments; it loads or seeds the pool and registers it once.
- `StrategyPoolJsonService` takes the pool through its constructor (`StrategyPool? pool = null, BonesStrategyLibrary? library = null`), exposes `Pool` for the uninitialized check, and every builder reads `_pool`.
- `BonesWebHost.MapPoolRoutes` checks `service.Pool is null` instead of the static for its 503 `PoolUninitialized` responses (six routes).
- `BonesCoLearningEnhanceAgent` requires the injected `StrategyPool` (no static fallback); a missing registration now fails DI resolution deterministically.
- Web tests: no static mutation. The two web-host factories publish the per-test pool through a `StrategyPoolHolder` registered in test DI (transient `StrategyPoolJsonService` reads the holder per request); the JSON-service and client-JS tests construct `StrategyPoolJsonService(pool)` directly. Host tests assert the DI singleton is identical from the root provider and a run scope, and that the co-learning agent fails deterministically when no pool is registered.
- Host README updated: the 503 uninitialized case now describes the DI pool.

## Verification

| Check | Result |
|---|---|
| `dotnet build Wip.slnx -c Release --no-restore` | exit 0 |
| `Wip.Bones.Web.Tests` (not in the offline gate) | 276 total / 267 passed / **9 pre-existing failures**: 6 `BehaviorProofComplianceGateTests` meta-gates, 2 Playwright DOM-rendering tests, 1 viewer-workflow test that fails in the stub model response (“did not contain a compilable IBonesPlayerSlot script”). None touches the pool path. |
| `StrategyPool.Instance` in code | 0 references (only historical docs and one requirement-label string remain) |
| Full offline gate | **green**: 16 projects, 1,104 executed / 1,081 passed / 23 failed (all 27-register waived), verifier exit 0; Host suite completed with only its 9 waived failures |

## Follow-on fixes from the first gate run

`RunWorkflowAsync_GivenBonesLearningWorkflow_ExpectedDispatchesAllStagesInOrderWithArtifacts` failed because `AddBonesLearningWorkflow` registers `BonesCoLearningEnhanceAgent`, which now requires `StrategyPool`, and the test service collection did not register one. Fixed by registering `new StrategyPool()` in the test builder (`RegisterBonesLearningServices`) and in the viewer-workflow test services. This is the intended contract: the host composition must register the pool, exactly as `AddBonesHostServices` does.
## Gate flake fix (compile budget)

The first full gate after the change failed five timing-sensitive tests, all with `Strategy script compilation timed out`. Measured on this machine: the first in-process Roslyn compile costs **~9-10 s** (later compiles ~0.8 s), while the tests’ compile budget was the 10 s default. Under the suite’s parallel load that crossed the budget. Production already configures `ScriptHost:CompilationTimeout = 30 s` and the worker client uses a 30 s compile timeout, so the default is now aligned to 30 s (and the two in-process timeout tests use the same explicit budget). No execution or security semantics changed.