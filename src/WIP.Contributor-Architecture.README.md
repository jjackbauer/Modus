# WIP Contributor Architecture

This document is the contributor map for the WIP stack. It identifies who owns each Wip.* project, the runtime role of that project, and the extension seams contributors can use without crossing module boundaries.

## Project Ownership and Runtime Role Map

| Project | Ownership | Runtime role | Extension seams |
|---|---|---|---|
| Wip.Abstractions | WIP Contracts Maintainers | Shared contract layer for workflow, capability, policy, and session APIs | ICapability<TRequest,TResult>, IWorkflow<TRequest,TResult>, IPolicy<TRequest>, ISessionStore |
| Wip.Artifacts.Local | WIP Runtime Maintainers | Local file artifact persistence used by runtime execution and reporting | Local artifact store interfaces and persistence services registered through DI |
| Wip.Builder | WIP Runtime Maintainers | Composition API that registers agents, tools, validators, policies, and workflows | WipBuilder.AddAgent/AddTool/AddValidator/AddPolicy/AddWorkflow and capability replacement controls |
| Wip.Modus | WIP Integration Maintainers | Bridge between Modus plugin discovery and WIP runtime manifest/diagnostics | IModusWipBridge, ModusWipBridge.LoadPluginsAsync, GetRunManifest, GetLoadDiagnostics |
| Wip.Policy.LocalSafe | WIP Safety Maintainers | Policy gate for dangerous operations, approval requirements, and worktree boundaries | LocalSafePolicy and LocalSafePolicyRequest operation semantics |
| Wip.Runtime | WIP Runtime Maintainers | Session lifecycle orchestration, state transitions, workflow stage execution, and approval/review helpers | WipRuntimeOrchestrator, WorkflowExecutionPipeline, ISessionEventPublisher |
| Wip.Shell | WIP Shell Maintainers | Interactive command dispatch loop for operator workflows | WipShellCommandLoop command handlers and command argument parser paths |
| Wip.ShellHost | WIP Shell Maintainers | Host/container bootstrap and config resolution for shell runtime startup | WipShellHostOptions.FromArgs, WipShellHostContainer, IWipShellEngine |
| Wip.Tools.Shell | WIP Tooling Maintainers | Tool capability that executes shell commands through WIP abstractions and policies | ShellCommandTool and request/result contracts |
| Wip.Validation.DotNet | WIP Validation Maintainers | Dotnet validation capability for build/test command execution and report shaping | DotNetValidationValidator and ValidationReport contracts |
| Wip.Workspaces.Git | WIP Workspace Maintainers | Git workspace provisioning, drift checks, and merge preview/merge execution | WipWorkspaceProviderGit and workspace/merge request/result records |

## Session Command Trace (transition planning)

Use this path when validating how a contributor command travels through runtime components:

1. Shell receives `transition planning` in src/Wip.Shell/Interactive/WipShellCommandLoop.cs.
2. Shell delegates transition execution to src/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs.
3. Policy checks for operation safety run through src/Wip.Policy.LocalSafe/LocalSafePolicy.cs before sensitive operations.
4. Runtime can surface plugin/workflow manifest diagnostics through src/Wip.Modus/Hosting/ModusWipBridge.cs.
5. State and workflow definitions consumed by runtime are registered through src/Wip.Builder/WipBuilder.cs and typed contracts in src/Wip.Abstractions.

## Contributor Extension Rules

- Add or evolve extension seams through Wip.Abstractions contracts first.
- Register new executable behavior through Wip.Builder, not direct runtime mutation.
- Keep shell commands thin; business progression belongs in Wip.Runtime.
- Treat Wip.Policy.LocalSafe deny reasons and gate semantics as stable contributor-facing behavior.
- Use Wip.Modus diagnostics and manifest APIs as the supported plugin observability surface.

## LocalSafe Policy Rules (Allow/Deny Examples)

Contributors should treat these examples as the behavioral contract for `Wip.Policy.LocalSafe.LocalSafePolicy`.

### Dangerous command gate

| Example | Request shape | Expected decision |
|---|---|---|
| Deny | `Command = "rm -rf ."`, `WorkingDirectory = <active-worktree>` | Deny with reason containing `dangerous command pattern` |
| Allow | `Command = "echo cleanup"`, `WorkingDirectory = <active-worktree>` | Allow when no other gate requires validation/approval |

### Worktree boundary gate

| Example | Request shape | Expected decision |
|---|---|---|
| Deny | `Command = "echo safe"`, `WorkingDirectory = <outside-active-worktree>` | Deny with reason containing `outside the active worktree boundary` |
| Allow | `Command = "echo safe"`, `WorkingDirectory = <active-worktree>` | Allow when command is non-dangerous and no other gate blocks |

### Validation gate

| Example | Request shape | Expected decision |
|---|---|---|
| Deny | `OperationName = "Wip.Runtime.Approve"`, `ValidationSucceeded = false` | Deny with reason containing `passing validation evidence is required` |
| Allow | `OperationName = "Wip.Runtime.Approve"`, `ValidationSucceeded = true` | Allow when command/worktree checks pass |

### Approval gate

| Example | Request shape | Expected decision |
|---|---|---|
| Deny | `OperationName = "Wip.Runtime.Merge"`, `ApprovalGranted = false`, `ValidationSucceeded = true` | Deny with reason containing `explicit approval evidence is required` |
| Allow | `OperationName = "Wip.Runtime.Merge"`, `ApprovalGranted = true`, `ValidationSucceeded = true` | Allow when command/worktree checks pass |

## Contributor Validation Workflow (Proof Artifacts Required)

Contributors must attach deterministic command evidence for build, test, and runtime negative-path validation before a checklist transition from `[ ]` to `[x]` is accepted.

### Required validation commands

1. Build proof command

```powershell
dotnet build src/Wip.Modus/Wip.Modus.csproj -v minimal
```

Expected success signal: `Build succeeded.`

2. Test proof command

```powershell
dotnet test tests/Wip.Modus.Tests/Wip.Modus.Tests.csproj -v minimal
```

Expected success signal: `Passed!`

3. Runtime negative-path proof command

```powershell
dotnet test tests/Wip.Modus.Tests/Wip.Modus.Tests.csproj --filter "FullyQualifiedName~ContributorWorkflowReadme_GivenIntentionalRuntimeFailure_NegativePathEvidenceCapturedAndLinkedInChecklist" -v minimal
```

Expected success signal: the targeted test passes after asserting deterministic runtime failure semantics (`[discovery]` load diagnostic for a missing plugin path).

### Required proof artifacts

All three files are mandatory and must be committed for the checklist item transition:

- `.github/requirements/proof-artifacts/wip-modus-contributor-validation/build-wip-modus.log`
- `.github/requirements/proof-artifacts/wip-modus-contributor-validation/test-wip-modus.log`
- `.github/requirements/proof-artifacts/wip-modus-contributor-validation/runtime-negative-path.log`

### Deterministic negative-path check contract

- The runtime-negative artifact must show the targeted test name `ContributorWorkflowReadme_GivenIntentionalRuntimeFailure_NegativePathEvidenceCapturedAndLinkedInChecklist` with a passing status.
- The negative-path assertion must come from public API behavior (`ModusWipBridge.LoadPluginsAsync` + `GetLoadDiagnostics`) rather than private implementation details.
- Missing any required artifact blocks checklist completion.
