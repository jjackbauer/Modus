# WP-2.1 â€” Host-managed DI

**Status:** implementation complete 2026-11-04; verification pending the gate.

## Delivered

- `WipRuntimeOrchestrator.RunWorkflowAsync` now takes a required `IServiceProvider services` and resolves every capability and the artifact store from it. The nested `builder.Services.BuildServiceProvider()` is gone; the runtime never builds a container.
- Hosts own composition and the run scope:
  - `BonesLearningLoopHost` takes `IServiceProvider` from DI and opens `await using var runScope = _services.CreateAsyncScope()` per iteration.
  - `WipShellCommandLoop` accepts an optional host provider; when none is supplied it builds one provider **once** from the builder's services (host behavior) and opens a scope per `run` plus per provider-plan resolution. It disposes a provider it owns.
- All 21 call sites updated: production hosts plus `Wip.Runtime.Tests`, `Wip.Builder.Tests`, `Wip.Bones.Tests`, `Wip.Bones.Web.Tests`; tests pass a provider built from the builder (host-equivalent).
- New tests:
  - `RunWorkflowAsync_GivenCallerOwnedProvider_ExpectedAllResolutionsGoThroughIt` wraps the host provider in a recording `IServiceProvider` and asserts the run resolves `WorkflowRunTool` and `IArtifactStore` **through it** â€” a nested provider would resolve from nowhere and never touch the wrapper.
  - `RuntimeSource_GivenBuildServiceProvider_ExpectedNoNestedProvider` (architecture project) scans `WIP/Wip.Runtime` and fails if `BuildServiceProvider` reappears. Architecture suite is now 4 tests.

## Effect

- The per-run nested container (which also instantiated a second `BonesScriptHostRuntime` and therefore a second sandbox worker â€” RISK-001) is removed. The run scope resolves the host's singletons.
- Test totals: `Wip.Runtime.Tests` 72 â†’ 73 with the new test; the one pre-existing cancellation flake remains waived.

## Verification

(pending gate run)