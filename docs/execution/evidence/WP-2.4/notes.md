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

## Increment 2 — process-wide registry deleted (2026-11-04)

- `WorkflowStageMapAdapterRuntime` no longer exposes `RegisterInitialRequestFactory` /
  `RegisterMapBinding` / `ResetRegisteredBindingsForTests` and holds no mutable lists. The P1-03
  race source is gone.
- `BonesLearningWorkflowMapRuntime` is now only the shared request-builder helper; `Register`,
  `ResetRegistrationForTests`, the initial-request factory and the legacy `CompileLinearStages`
  were removed from Bones.
- Tests migrated to the plan: the reliability agent tests call
  `WorkflowPlanMapRuntime.ApplyMappedStageInput` with `BonesLearningWorkflow.Compile()`; the
  registration test asserts the plan stages/maps; the per-stage model tests no longer touch the
  registry; the viewer workflow tests dropped their reset calls.
- The serialized `BonesWorkflowMapRuntimeCollection` and its `DisableParallelization` were deleted;
  the affected classes run in the normal parallel schedule again.

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
## Increment 3 — legacy linear workflows run on explicit plans (2026-11-05)

- **`WorkflowRegistration.CreateRunRequest`** (`Func<WorkflowContext, object>`) is now part of every
  registration. `AddWorkflow<TWorkflow,TRequest,TResult>(...)` accepts an optional typed factory;
  the default (documented convention) builds the request from the session task via its `string`
  constructor, or its parameterless constructor, and fails with a clear message otherwise.
- **`LinearWorkflowPlanFactory`** declares the six governance stages and all five maps explicitly:
  Plan→Run uses the registration's factory, then Run→Validate, Validate→Review,
  Review→RequireApproval and RequireApproval→Merge construct their governance requests from the
  session id. No reflection property heuristics, no global registry.
- **Orchestrator**: definitions keep their own plan; legacy `IWorkflow` registrations receive the
  explicit default plan and every stage input flows through `WorkflowPlanMapRuntime`. The old
  `WorkflowStageMapAdapterRuntime` is **deleted** (its evidence record moved to
  `WorkflowPlanMapRuntime`). The typed builder/compiler remains for stage descriptors.
- **Tests**: the three manual `WorkflowRegistration` constructions now pass explicit factories; all
  existing workflow tests exercise the plan path for legacy registrations.
- This closes WP-2.4: the only stage-mapping mechanism left is plan-scoped `StageMap` delegates.