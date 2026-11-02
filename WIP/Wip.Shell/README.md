# Wip.Shell Contributor README

Wip.Shell provides the interactive command loop for WIP sessions. It parses command input, enforces command usage contracts, forwards runtime actions to the orchestrator, and surfaces plugin/workflow diagnostics through the Modus bridge when available.

Dependency note: runtime lifecycle semantics are owned by Wip.Runtime. See `WIP/Wip.Runtime/README.md` for session-state transition contracts.

Authoritative command semantics live in `Wip.Shell.CommandSemantics.WipShellCommandSemantics` (`WIP/Wip.Shell/Interactive/WipShellCommandSemantics.cs`) and are rendered by the `help` command.

## Command Surface

`WipShellCommandLoop` supports these commands:

| Command | Usage | Behavior |
|---|---|---|
| `help` | `help` | Prints the authoritative command list and lifecycle semantics contract. |
| `init` | `init` | Creates the shell-owned `.wip`, `.wip/sessions`, `.wip/worktrees`, and `.wip/plugins` directories under the current repository root when they do not already exist. |
| `repo` | `repo` | Prints the current repository root, `.wip` paths, selected workflow, and active-session summary when present. |
| `sessions` | `sessions` | Lists persisted session snapshots discovered under `.wip/sessions`. |
| `session start` | `session start "<task>"` | Starts a new session using the active repository root plus runtime-owned session metadata for workflow, worktree, branch, commit, and artifact paths, then attaches it to the shell prompt. |
| `session attach` | `session attach <session-id>` | Attaches an existing session by persisted session id and restores its runtime-owned metadata from `.wip/sessions/<session-id>`. |
| `use workflow` | `use workflow <id>` | Selects the workflow id used for subsequent `session start` and `run` commands. |
| `plan` | `plan` | Prepares the active session for execution by advancing `Created` to `Editing`. Does not execute workflow stages or complete governance. |
| `run` | `run` | Executes registered workflow **Plan** and **Run** stages only (execution-only). Does **not** replace `validate`, `review`, `approve`, or `merge`, and does **not** complete the governance lifecycle. |
| `diff` | `diff` | Prints the current active-session repository/worktree context and the files currently present in the worktree. |
| `checkpoint` | `checkpoint` | Prints the persisted `session-state.json` and `event-journal.ndjson` paths for the active session. |
| `validate` | `validate` | **Governance command:** records validation evidence, updates `ValidationStatus`, and advances session state when prerequisites are met. |
| `review` | `review` | **Governance command:** records review evidence for the current diff and advances session state toward approval. |
| `approve` | `approve` | **Governance command:** creates an approval-token artifact and sets `ApprovalStatus` when confirmation succeeds. |
| `merge` | `merge` | **Governance command:** merges approved work into the target branch and advances session state to `Merged` when required artifacts exist. |
| `status` | `status` | Prints session metadata and governance lifecycle guidance when explicit governance commands are still required. |
| `artifacts` | `artifacts` | Lists persisted runtime artifacts under `.wip/sessions/<session-id>`. |
| `detach` | `detach` | Detaches the current session; repeated detach returns no-op status text. |
| `archive` | `archive [--mark-only|--cleanup]` | Clears a merged session from the active shell prompt; `--mark-only` preserves the worktree, `--cleanup` removes the worktree while preserving persisted state and artifacts. |
| `abort` | `abort` | Clears a non-merged session from the active shell prompt while leaving persisted state on disk for later recovery. |
| `plugins` | `plugins` | Prints plugin manifest entries and plugin load diagnostics from bridge metadata. |
| `workflows` | `workflows` | Prints registered workflow descriptors from bridge manifest metadata. |
| `exit` / `quit` | `exit` | Stops the interactive loop and returns shell host exit code 0. |

### Execution vs governance boundary

| Category | Commands | Contract |
|---|---|---|
| Execution preparation | `plan` | Moves the session into an editable execution posture without running workflow stages. |
| Workflow execution | `run` | Dispatches compiled Plan and Run workflow stages and persists execution artifacts. Stops short of governance completion. |
| Governance lifecycle | `validate`, `review`, `approve`, `merge` | Each command records evidence and advances governance state explicitly. None are implied by `run`. |
| Runtime transitions | `TransitionAsync` (runtime API) | **Not** exposed as a shell command. Shell users must use the governance commands above. |

## Deterministic Failure Messages

The command loop provides stable failure text for contributor verification:

| Failure case | Expected message |
|---|---|
| Unknown command | `Unknown command '<command>'. Use 'help' to list commands.` |
| Invalid session start syntax | `Usage: session start "<task>"` |
| Invalid session attach syntax | `Usage: session attach <session-id>` |
| Invalid workflow selection syntax | `Usage: use workflow <id>` |
| Session-required command with no active session | `This command requires an active session. Use 'session start "<task>"' or 'session attach <session-id>'.` |
| Out-of-order lifecycle command | `Command '<command>' requires session state <expected>. Current state is <actual>.` |
| Run without a configured builder | `Cannot run workflow: no active builder is configured for this shell instance.` |
| Archive before merge | `Archive requires session state Merged. Current state is <state>.` |
| Abort after merge | `Merged sessions cannot be aborted. Use 'archive' to clear the active shell session.` |

## Diagnostics Bridge Behavior

Diagnostics commands are bridge-aware:

1. `plugins` reads `GetRunManifest()` and `GetLoadDiagnostics()` from `IModusWipBridge`.
2. Output always includes manifest capture timestamp, then loaded plugin metadata when present.
3. If no plugins are loaded, output includes `No plugins are currently loaded.`.
4. If diagnostics exist, output includes `Plugin diagnostics:` followed by each diagnostic entry.
5. If no diagnostics bridge is provided, command output is deterministic:
   - `Plugin diagnostics are unavailable in this shell instance.`
   - `Workflow diagnostics are unavailable in this shell instance.`

Contributors changing command names, usage syntax, lifecycle semantics, or failure text must update this README, `WipShellCommandSemantics`, and the behavior-proof tests in `WIP/Wip.Shell.Tests` and `WIP/Wip.Shell.E2E.Tests`.
