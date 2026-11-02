# Wip.Runtime Contributor README

Wip.Runtime is the session-lifecycle orchestrator for WIP execution. It applies deterministic state transitions, persists session snapshots, restores sessions on attach, and executes workflow stages in a fixed progression.

Dependency note: workflow registrations consumed by the runtime are produced by Wip.Builder. See `WIP/Wip.Builder/README.md` for registration contracts and workflow-selection constraints.

Shell command semantics for execution vs governance boundaries are defined in `WIP/Wip.Shell/Interactive/WipShellCommandSemantics.cs` and surfaced through shell `help` and `status`.

## Orchestrator Lifecycle

`WipRuntimeOrchestrator` coordinates runtime progression through these APIs:

| API | Lifecycle contract | Deterministic outcome |
|---|---|---|
| `StartSessionAsync` | Creates a new session snapshot in `Created` state, fills task and repository metadata, and emits a start event. | Persisted snapshot exists at `.wip/sessions/{sessionId}/session-state.json` with task, workflow, branch, commit, worktree, artifact, and governance fields, and `SessionStarted` is published. |
| `TransitionAsync` | Enforces linear next-state transitions only. | Invalid jumps throw `InvalidOperationException` with expected-next-state details. Not exposed as an interactive shell command. |
| `RunWorkflowAsync` | Executes compiled workflow **Plan** and **Run** stages for shell `run` (execution-only). | Dispatches Plan and Run capabilities, persists stage execution artifacts, and leaves governance completion to explicit `validate`, `review`, `approve`, and `merge` shell commands. |
| `ValidateAsync` | Governance validation path invoked by shell `validate`. | Records validation evidence, updates `ValidationStatus`, and persists a `validation-report` artifact. |
| `CreateApprovalTokenAsync` | Governance approval path invoked by shell `approve`. | Records an `approval-token` artifact and aligns `ApprovalStatus`. |
| `MergeAsync` | Governance merge path invoked by shell `merge`. | Records merge evidence and advances to `Merged` when prerequisites are satisfied. |
| `AttachSessionAsync` | Restores an existing session from in-memory store or persisted snapshot. | Rehydrates snapshot, tracks attached session, and emits `SessionAttached`. |
| `DetachSessionAsync` | Clears currently attached session context. | Returns `true` once when a session is detached, then `false` for repeated detach calls. |

## State Transition Model

Runtime transitions are strict and linear:

`Created -> Editing -> Validating -> AwaitingApproval -> Approved -> Merged`

Transition attempts that skip or reorder this sequence are rejected.

Shell users advance governance through explicit commands (`validate`, `review`, `approve`, `merge`). The `run` command executes workflow stages but does **not** substitute for those governance commands.

## Persisted Session Snapshots

Each session mutation persists a JSON snapshot under the repository root:

`.wip/sessions/{sessionId}/session-state.json`

Snapshot payload fields:

- `SessionId`
- `WorkflowId`
- `State`
- `RepositoryPath`
- `WorktreePath`
- `TaskDescription`
- `BaseBranch`
- `BaseCommit`
- `TargetBranch`
- `TargetCommit`
- `ArtifactDirectory`
- `ValidationStatus`
- `ApprovalStatus`
- `UpdatedAtUtc`

Persistence occurs when a session starts, after every successful transition, and whenever validation or approval metadata changes.

## Event Journal Records

Runtime writes append-only event records for each session lifecycle operation at:

`.wip/sessions/{sessionId}/event-journal.ndjson`

Each line is a JSON object containing:

- `Kind`
- `SessionId`
- `CurrentState`
- `PreviousState`
- `OccurredAtUtc`
- `Message`

The journal records `SessionStarted`, `SessionTransitioned`, `SessionAttached`, and `SessionDetached` in call order.

## Attach and Detach Flow

1. `AttachSessionAsync(repositoryPath, sessionId)` resolves from in-memory store first.
2. If not in memory, runtime loads `.wip/sessions/{sessionId}/session-state.json`.
3. Restored snapshot is saved into the active store with task, branch, commit, worktree, artifact, validation, and approval metadata intact, and `SessionAttached` is emitted.
4. `DetachSessionAsync()` clears the attached session and emits `SessionDetached`.
5. A second detach without re-attach returns `false` and does not emit additional detach state mutation.

## Workflow Execution Boundary

`RunWorkflowAsync` (shell `run`) is **execution-only**:

| Stage executed by `run` | Purpose | Governance substitute? |
|---|---|---|
| Plan | Compile and execute the planning stage; advance to `Editing` when required. | No |
| Run | Dispatch tool/agent capabilities for the run stage; persist execution artifacts. | No |

Governance stages require explicit shell commands and artifact-backed operations:

| Governance stage | Shell command | Runtime API | Evidence artifact |
|---|---|---|---|
| Validate | `validate` | `ValidateAsync` | `validation-report-*` |
| Review | `review` | review generator path | `review-report-*` |
| Require approval | `approve` | `CreateApprovalTokenAsync` | `approval-token-*` |
| Merge | `merge` | `MergeAsync` | `merge-result-*` |

`run` does not replace validate, review, approve, or merge, and does **not** complete the governance lifecycle on its own.

Contributors changing workflow stage order, execution boundaries, or governance mapping must update this document, `WipShellCommandSemantics`, and runtime behavior tests in `WIP/Wip.Runtime.Tests`.
