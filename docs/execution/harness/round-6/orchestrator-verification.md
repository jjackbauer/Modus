# Round 6 — Orchestrator verification of WP6 (legacy workflow shim retired)

- **Round**: 6 (goal WIP-IMPL-001 continuation, D6)
- **WP6 deliverable**: `docs/execution/harness/round-6/implementation-wp6.md`
- **Starting HEAD**: `7158280` (WP5)
- **Result**: PASS — the legacy Bones `IWorkflow<,>` implementation/registration and stub
  `ExecuteAsync` are gone, the definition plan is the only path, and the full Bones/Runtime/Shell/
  Builder/architecture suites show only pre-existing waived/platform failures.

## 1. What changed

- `WipBuilder.AddDeclarativeWorkflow<TDefinition,TRequest,TResult>(WorkflowId, string)` registers the
  definition singleton (same instance behind `IWorkflowDefinition`) and exactly one
  `WorkflowRegistration` for selection, with a run-request factory that throws because the definition
  plan supplies the initial request. `AddWorkflowDefinition` is unchanged.
- `BonesLearningWorkflow` now implements only `IWorkflowDefinition`; the stub `ExecuteAsync` and its
  `workflow-dispatch` ids are deleted; `Compile()` is unchanged.
- `BonesLearningWorkflowRegistration` calls the new method instead of `AddWorkflow` +
  `AddWorkflowDefinition`; the four capability registrations are untouched.
- The one test helper that reproduced the legacy registration is switched to the new method; no test
  was deleted. Two builder tests and one runtime declarative-only end-to-end test were added.

## 2. Grep proof

`git grep -n -E "IWorkflow<BonesLearningWorkflow|AddWorkflow<BonesLearningWorkflow|AddWorkflowDefinition<BonesLearningWorkflow|workflow-dispatch" -- WIP`
prints nothing (`GREP_EXIT=1`); `BonesLearningWorkflow.cs` declares
`public sealed class BonesLearningWorkflow : IWorkflowDefinition`.

## 3. Suites (orchestrator reproduction, main tree unless noted)

| Suite | Observed | Notes |
|---|---|---|
| `Wip.Bones.Tests` | **455 total, 454 passed, 1 failed** | the failure is the pre-existing waived `BonesBoardOrientationBehaviorProofComplianceGateTests...`; the implementer's run had 455/455 (that meta-test is flaky and waived) |
| `Wip.Runtime.Tests` | **153 total, 147 passed, 6 failed** | the six are the documented 5 Windows-only `dotnet.cmd` tests + the waived cancellation test; 153 = WP5 152 + the new declarative-only runtime test |
| `Wip.Shell.Tests` | **34/34** | unchanged |
| `Wip.Builder.Tests` | **34/34** | 32 declared + the 2 new `AddDeclarativeWorkflow` tests |
| `Wip.Architecture.Tests` (clean 43-project clone at `7158280`) | **4/4** | WP6 adds no projects or references |
| Host/Web builds | 0 errors (implementer-observed) | one transient `MSB3021` cleared on retry |

## 4. Pre-existing viewer failure control

`Wip.Bones.Web.Tests.BonesViewerWorkflowTests` shows 1 failed / 1 passed on the **pre-WP6** clone at
`7158280` (`.tmp/wp6-ctrl/viewer-pre.trx`), with the same
`BonesViewerWorkflow_GivenLearningRun_ExpectedShellStdoutContainsViewerUrl` capability-resolution
failure. It is therefore pre-existing and independent of the WP6 diff; `Wip.Bones.Web.Tests` is not in
the 16-project offline gate.

## 5. Scope / integrity

- No existing test was deleted or weakened; the legacy-registration helper was rewritten to the
  declarative registration, preserving the test's intent.
- The co-learning agents, prompts, host composition, provider project and the concurrent Tholdem work
  are untouched.
- No implementer commit was made; this round's files are staged by the orchestrator.
