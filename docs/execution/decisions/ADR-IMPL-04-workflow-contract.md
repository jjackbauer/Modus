# ADR-IMPL-04 — Workflow definition and executor contract

- **Status:** Proposed (G4 decision required)
- **Date:** 2026-11-02
- **Owner:** tech lead + runtime lead
- **Reversal cost:** Medium

## Context

`IWorkflow.ExecuteAsync` is a public contract the runtime never calls; workflows compile to stage descriptors and maps are resolved by global heuristics. This is misleading and prevents two plans with the same types from coexisting.

## Decision

A workflow has one of two explicit shapes:

- **Declarative:** implement `IWorkflowDefinition` and return a `WorkflowPlan` with typed stages and explicit `StageMap` delegates; `LinearWorkflowExecutor` runs it.
- **Custom:** implement `IWorkflowExecutor` for branching/loops; it receives `WorkflowExecutionContext` (session, host services, artifacts, policy) and is responsible for stage invocation and evidence.
- `IWorkflow` remains as an `[Obsolete]` one-release adapter over `IWorkflowDefinition`.
- `StageMap` delegates are plan-scoped; no process-global map registry or property-name fallback.

## Consequences

- Positive: the registered contract is the executed contract; map collisions disappear; custom flows are explicit.
- Negative: migration touches every workflow registration and the Bones workflow; one release of duplicated adapter code.

## Reversal triggers

- A custom workflow cannot be expressed without host internals.
- The adapter cannot preserve existing consumers for one release.

## Open questions

- Does `WorkflowExecutionContext.Services` expose the host scope or a restricted facade? Owner: runtime lead.
- What is the stage-artifact contract? Owner: tech lead.
