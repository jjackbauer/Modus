# WIP Contributor Architecture

This document is the contributor map for the WIP stack. It identifies who owns each Wip.* project, the runtime role of that project, and the extension seams contributors can use without crossing module boundaries.

## Project Ownership and Runtime Role Map

| Project | Ownership | Runtime role | Extension seams |
|---|---|---|---|
| Wip.Abstractions | WIP Contracts Maintainers | Shared contract layer for workflow, capability, policy, and session APIs | ICapability<TRequest,TResult>, IWorkflow<TRequest,TResult>, IPolicy<TRequest>, ISessionStore |
| Wip.Agent.Basic | WIP Runtime Maintainers | Packable baseline agents for runtime composition and deterministic sample execution paths | PlanOnlyAgent, PatchAgent, AgentExecutionContext |
| Wip.Artifacts.Local | WIP Runtime Maintainers | Local file artifact persistence used by runtime execution and reporting | Local artifact store interfaces and persistence services registered through DI |
| Wip.Builder | WIP Runtime Maintainers | Composition API that registers agents, tools, validators, policies, and workflows | WipBuilder.AddAgent/AddTool/AddValidator/AddPolicy/AddWorkflow and capability replacement controls |
| Wip.Modus | WIP Integration Maintainers | Bridge between Modus plugin discovery and WIP runtime manifest/diagnostics | IModusWipBridge, ModusWipBridge.LoadPluginsAsync, GetRunManifest, GetLoadDiagnostics |
| Wip.Policy.LocalSafe | WIP Safety Maintainers | Policy gate for dangerous operations, approval requirements, and worktree boundaries | LocalSafePolicy and LocalSafePolicyRequest operation semantics |
| Wip.Runtime | WIP Runtime Maintainers | Session lifecycle orchestration, state transitions, workflow stage execution, and approval/review helpers | WipRuntimeOrchestrator, WorkflowExecutionPipeline, ISessionEventPublisher |
| Wip.Shell | WIP Shell Maintainers | Interactive command dispatch loop for operator workflows | WipShellCommandLoop command handlers and command argument parser paths |
| Wip.Shell.E2E | WIP Integration Maintainers | Shared E2E compliance registry, aggregated behavior-proof gates, and E2E matrix execution orchestration | BehaviorProofComplianceRegistry, E2EMatrixComplianceRegistry, aggregated compliance gate execution |
| Wip.ShellHost | WIP Shell Maintainers | Host/container bootstrap and config resolution for shell runtime startup | WipShellHostOptions.FromArgs, WipShellHostContainer, IWipShellEngine |
| Wip.Tools.Shell | WIP Tooling Maintainers | Tool capability that executes shell commands through WIP abstractions and policies | ShellCommandTool and request/result contracts |
| Wip.Validation.DotNet | WIP Validation Maintainers | Dotnet validation capability for build/test command execution and report shaping | DotNetValidationValidator and ValidationReport contracts |
| Wip.Workspaces.Git | WIP Workspace Maintainers | Git workspace provisioning, drift checks, and merge preview/merge execution | WipWorkspaceProviderGit and workspace/merge request/result records |

## Contributor Onboarding Quickstart

Reproducible local shell transcript. Build the host once, then run from a git repository root:

```powershell
dotnet build WIP/Wip.ShellHost/Wip.ShellHost.csproj -v minimal
dotnet WIP/Wip.ShellHost/bin/Debug/net10.0/Wip.ShellHost.dll
```

### Bootstrap and execution path

| Step | Command | Expected stdout marker |
|---|---|---|
| Initialize workspace | `init` | `Repository initialized:` |
| Select workflow | `use workflow workflow.linear` | `Workflow selected: workflow.linear` |
| Start session | `session start "Contributor onboarding"` | `Session started:` |
| Prepare execution | `plan` | `Plan generated: workflow=workflow.linear state=Editing` |
| Execute workflow stages | `run` | `Workflow executed: workflow.linear` |
| Inspect lifecycle guidance | `status` | `Active session state:` |

### Attach and governance path

After creating and committing a worktree change, reattach and complete governance explicitly:

| Step | Command | Expected stdout marker |
|---|---|---|
| Reattach session | `session attach <session-id>` | `Session attached:` |
| Inspect diff context | `diff` | `worktree:` |
| Record validation evidence | `validate` | `Validation completed: session=` |
| Record review evidence | `review` | `Review generated: session=` |
| Record approval evidence | `approve` then `yes` | `Approval completed: session=` |
| Merge approved work | `merge` | `Merge completed: session=` |

`run` does not replace `validate`, `review`, `approve`, or `merge`. Governance commands remain mandatory even when workflow stages execute successfully.

### Diagnostics commands

| Command | Expected stdout marker | Semantics |
|---|---|---|
| `config` | `configSource:` | Renders effective `.wip/config.json` hydration including repository, workspace, plugins, sessions, and worktrees paths. |
| `plugins` | `Loaded plugins:` or `No plugins are currently loaded.` | Lists bridge manifest plugin entries with capability and permission inventory, plus `[discovery]` load diagnostics when present. |
| `workflows` | `Registered workflows:` | Lists builder-registered workflow ids with request/result contract names. |
| `debug-logs` | `Host run correlation:` | Prints Modus debug snapshot entries as `[level] source :: message (run=...)` or `No Modus debug logs were emitted for the current host run.` |

## Plugin and Workflow Diagnostics Reference

Contributors should treat these shell commands as the supported observability surface for plugin and workflow state.

### `plugins`

- When plugins are loaded, output begins with `Loaded plugins:` followed by one block per plugin: `capabilities:` and `permissions:` lines.
- When no plugins are loaded, output is exactly `No plugins are currently loaded.`
- Failed discovery/load paths surface under `Plugin diagnostics:` with `[discovery]` or `[run:...]` prefixed entries from `ModusWipBridge.GetLoadDiagnostics()`.

### `workflows`

- Output begins with `Registered workflows:` and lists each workflow as `- <workflow-id> [<display-name>] request=<type> result=<type>`.
- When the builder has no registrations, output is `No workflows are currently registered.`

### `config`

- `repo` and `config` together expose hydrated paths and `configSource: disk` when `.wip/config.json` is present.
- CLI `--plugins-path` overrides config file values in effective configuration output.

### `debug-logs`

- Always prints `Host run correlation: <id>` when the Modus debug channel is available.
- Emits `Modus debug logs:` with deterministic sorted entries, or `No Modus debug logs were emitted for the current host run.` when the snapshot is empty.

## Session Command Trace (transition planning)

Use this path when validating how a contributor command travels through runtime components:

1. Shell receives `transition planning` in WIP/Wip.Shell/Interactive/WipShellCommandLoop.cs.
2. Shell delegates transition execution to WIP/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs.
3. Policy checks for operation safety run through WIP/Wip.Policy.LocalSafe/LocalSafePolicy.cs before sensitive operations.
4. Runtime can surface plugin/workflow manifest diagnostics through WIP/Wip.Modus/Hosting/ModusWipBridge.cs.
5. State and workflow definitions consumed by runtime are registered through WIP/Wip.Builder/WipBuilder.cs and typed contracts in Wip.Abstractions.

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
dotnet build WIP/Wip.Modus/Wip.Modus.csproj -v minimal
```

Expected success signal: `Build succeeded.`

2. Test proof command

```powershell
dotnet test WIP/Wip.Modus.Tests/Wip.Modus.Tests.csproj -v minimal
```

Expected success signal: `Passed!`

3. Runtime negative-path proof command

```powershell
dotnet test WIP/Wip.Modus.Tests/Wip.Modus.Tests.csproj --filter "FullyQualifiedName~ContributorWorkflowReadme_GivenIntentionalRuntimeFailure_NegativePathEvidenceCapturedAndLinkedInChecklist" -v minimal
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
