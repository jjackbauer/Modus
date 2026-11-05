# WP-2.3 — Workflow definition/executor (part 1)

**Status:** in progress 2026-11-04. Part 1: definition contracts, declarative plan execution, explicit plan maps.

## Delivered (part 1)

- **Contracts moved to `Wip.Abstractions.Workflows`:** `WorkflowStageKind`, `WorkflowStageDescriptor`,
  `WorkflowStageExecution`, `WorkflowExecutionResult`. The builder may not reference `Wip.Runtime`
  (architecture rule), so the shared workflow vocabulary now lives in Abstractions.
- **New contracts:** `IWorkflowDefinition` (`WorkflowId`, `Compile()`), `WorkflowPlan`
  (typed stage list + plan-scoped `StageMap` list), `StageMap` / `StageMap<TSource,TTarget>` with a
  typed delegate `Func<TSource, WorkflowContext, TTarget>`.
- **`WorkflowPlanMapRuntime`:** applies a plan's maps with no process-global state, no property-name
  heuristic and no string-constructor fallback. A missing or mismatched map throws a deterministic
  error; the Plan governance request is still constructed by the runtime.
- **`WipBuilder.AddWorkflowDefinition<TDefinition>()`** registers the definition as a DI singleton;
  the orchestrator resolves definitions from the run provider and uses the first matching
  `WorkflowId`.
- **Orchestrator:** when a definition matches the selected workflow it compiles the plan, derives
  the stage descriptors from it and applies the plan maps for mapped input; otherwise the legacy
  `IWorkflow` registration path (reflection `CompileLinearStages` + registry/heuristics) runs
  unchanged. This keeps every existing workflow working while the migration proceeds.
- **Test:** `RunWorkflowAsync_GivenDeclarativeWorkflowDefinition_ExpectedPlanMapsDriveEveryStage`
  runs the six-stage linear plan through the definition path; the Plan→Run `StageMap` supplies the
  run request and the producing capabilities are asserted.

## Remaining for WP-2.3

- `IWorkflowExecutor` / `WorkflowExecutionContext` / `LinearWorkflowExecutor`: extract the stage
  loop so a custom executor can be registered, and mark `IWorkflow<TRequest,TResult>` obsolete as
  the legacy adapter.
- Then WP-2.4 migrates all workflows (including Bones) to definitions with explicit maps and
  deletes `WorkflowStageMapAdapterRuntime` (registry + heuristics).

## Verification

`dotnet build Wip.slnx -c Release --no-restore` exit 0. Gate: pending.
## Part 2 — executor extraction (2026-11-04)

- **Contracts:** `IWorkflowExecutor`, `WorkflowExecutionContext`, `IWorkflowRunAuthority`. The context carries the workflow id, session snapshot, builder, run provider, artifact store, correlation id, compilation, the stage-input mapper (plan or legacy) and the authority.
- **`LinearWorkflowExecutor`** now owns the stage loop: dispatch, mapped input, state advancement through the authority, per-stage evidence, and the terminal `WorkflowExecuted` event. The orchestrator implements the authority interface (advance under the authority gate, artifact reference shape, event publication) and `RunWorkflowAsync` builds the context and delegates.
- **Custom executors:** `RunWorkflowAsync` resolves an `IWorkflowExecutor` from the run provider; when absent it uses `LinearWorkflowExecutor`. A host or test registers its own executor as a singleton.
- **Tests:** `RunWorkflowAsync_GivenCustomExecutor_ExpectedExecutorOwnsTheRun` (a recording executor is invoked and its result is returned) alongside the declarative plan test; the legacy `IWorkflow` path remains covered by the existing suite. `Wip.Runtime.Tests` 75 total (74 passed, 1 pre-existing waived cancellation flake).
- **Remaining (deliberate):** `IWorkflow<TRequest,TResult>` is not yet marked `[Obsolete]`; the attribute lands with WP-2.5 when Bones migrates and usages collapse. Everything else in WP-2.3 acceptance is met.

## Verification

Build green. Full offline gate: 15/16 projects clean; the only unwaived failures are the two `BonesHostViewerLearningLoopStatus` Playwright tests (fixed 30 s browser waits starved by this machine's ~10 s first-compile slowdown). Both pass on CI for the same code paths; CI is the authoritative verification for this commit.