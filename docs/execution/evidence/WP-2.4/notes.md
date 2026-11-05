# WP-2.4 / WP-2.5 — plan-scoped maps and the Bones definition (increment 1)

**Status:** in progress 2026-11-04. Production Bones now runs from an explicit plan; the legacy
static map registry is retained only for legacy/default-linear test fixtures.

## Delivered

- **`WorkflowPlan.InitialRequest`** (`Func<SessionSnapshot, object?>?`). `WorkflowPlanMapRuntime`
  uses it when a plan's first stage is not the governance Plan stage, replacing the old
  process-wide `RegisterInitialRequestFactory` for definition-based workflows. A missing initial
  request or a type mismatch fails deterministically.
- **`WorkflowContext.RepositoryPath`** (optional, defaults to empty). Stage maps can construct
  requests that need the repository path.
- **`BonesLearningWorkflowParameters.TryParse(string? taskDescription, ...)`** overload so maps
  can parse parameters from `WorkflowContext.Task`; the session overload delegates to it.
- **Bones is a declarative definition**: `BonesLearningWorkflow.Compile()` returns the four stages
  (Observe-Run, Ponder-Plan, Play-Run, Enhance-Plan), three explicit `StageMap`s (Observe→Ponder,
  Ponder→Play, Play→Enhance) and an initial request factory. The map bodies live in
  `BonesLearningWorkflowMapRuntime` as shared internal builders, so the legacy registration and the
  definition use one implementation.
- **Registration:** `AddBonesLearningWorkflow` now calls `AddWorkflowDefinition<BonesLearningWorkflow>()`.
  The orchestrator resolves definitions from the run provider before the legacy compilation, so
  production Bones executes through the plan maps.
- **Test:** `AddBonesLearningWorkflow_GivenBuilder_ExpectedRegistersDefinitionWithExplicitPlanAndInitialRequest`
  asserts the definition is registered with 4 stages, 3 maps, an initial request and the expected
  target types; the existing workflow-run tests (7/7) still pass.

## Remaining for WP-2.4 (explicitly tracked)

`WorkflowStageMapAdapterRuntime` still holds the legacy fallback (string-constructor/property-name
heuristics) and `BonesLearningWorkflowMapRuntime.Register()` still feeds it for the legacy
`CompileLinearStages` path. The remaining consumers are test fixtures that register default-linear
`IWorkflow` classes and the per-stage map tests (`BonesLearningWorkflowMapRuntimePerStageModelTests`,
`BonesLearningLoopReliabilityAgentTests`, `BonesLearningWorkflowRegistrationTests`). Deleting the
registry requires migrating those fixtures to explicit `StageMap`s or to definitions; that is the
next WP-2.4 increment. Their serialized collection remains until then.

## Verification

Build green; new test and the seven Bones workflow tests green; full offline gate: pending.