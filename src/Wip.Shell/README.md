# Wip.Shell Contributor README

Wip.Shell provides the interactive command loop for WIP sessions. It parses command input, enforces command usage contracts, forwards runtime actions to the orchestrator, and surfaces plugin/workflow diagnostics through the Modus bridge when available.

Dependency note: runtime lifecycle semantics are owned by Wip.Runtime. See src/Wip.Runtime/README.md for session-state transition contracts.

## Command Surface

`WipShellCommandLoop` supports these commands:

| Command | Usage | Behavior |
|---|---|---|
| `help` | `help` | Prints the full command list including shell and host extension commands. |
| `start` | `start <workflow-id> <repository-path> <worktree-path>` | Starts a new session and stores it as active in the shell loop. |
| `attach` | `attach <repository-path> <session-id>` | Attaches an existing session from persisted or in-memory runtime storage. |
| `detach` | `detach` | Detaches the current session; repeated detach returns no-op status text. |
| `status` | `status` | Prints active session id, state, and workflow id. |
| `transition` | `transition <Created|Editing|Validating|AwaitingApproval|Approved|Merged>` | Requests a runtime state transition for the active session. |
| `plugins` | `plugins` | Prints plugin manifest entries and plugin load diagnostics from bridge metadata. |
| `workflows` | `workflows` | Prints registered workflow descriptors from bridge manifest metadata. |
| `exit` / `quit` | `exit` | Stops the interactive loop and returns shell host exit code 0. |

## Deterministic Failure Messages

The command loop provides stable failure text for contributor verification:

| Failure case | Expected message |
|---|---|
| Unknown command | `Unknown command '<command>'. Use 'help' to list commands.` |
| Invalid start syntax | `Usage: start <workflow-id> <repository-path> <worktree-path>` |
| Invalid attach syntax | `Usage: attach <repository-path> <session-id>` |
| Invalid transition syntax | `Usage: transition <Created|Editing|Validating|AwaitingApproval|Approved|Merged>` |
| Session-required command with no active session | `This command requires an active session. Use 'start <workflow-id> <repository-path> <worktree-path>' or 'attach <repository-path> <session-id>'.` |
| Runtime transition failure | `Failed to transition session: <runtime exception message>` |

## Diagnostics Bridge Behavior

Diagnostics commands are bridge-aware:

1. `plugins` reads `GetRunManifest()` and `GetLoadDiagnostics()` from `IModusWipBridge`.
2. Output always includes manifest capture timestamp, then loaded plugin metadata when present.
3. If no plugins are loaded, output includes `No plugins are currently loaded.`.
4. If diagnostics exist, output includes `Plugin diagnostics:` followed by each diagnostic entry.
5. If no diagnostics bridge is provided, command output is deterministic:
   - `Plugin diagnostics are unavailable in this shell instance.`
   - `Workflow diagnostics are unavailable in this shell instance.`

Contributors changing command names, usage syntax, or failure text must update this README and behavior-proof tests in tests/Wip.Shell.E2E.Tests.