# Wip.Runtime Contributor README

Wip.Runtime is the session-lifecycle orchestrator for WIP execution. It applies deterministic state transitions, persists session snapshots, restores sessions on attach, and executes workflow stages in a fixed progression.

Dependency note: workflow registrations consumed by the runtime are produced by Wip.Builder. See src/Wip.Builder/README.md for registration contracts and workflow-selection constraints.

## Orchestrator Lifecycle

`WipRuntimeOrchestrator` coordinates runtime progression through these APIs:

| API | Lifecycle contract | Deterministic outcome |
|---|---|---|
| `StartSessionAsync` | Creates a new session snapshot in `Created` state and emits a start event. | Persisted snapshot exists at `.wip/sessions/{sessionId}/session-state.json` and `SessionStarted` event is published. |
| `TransitionAsync` | Enforces linear next-state transitions only. | Invalid jumps throw `InvalidOperationException` with expected-next-state details. |
| `RunWorkflowAsync` | Maps workflow stages to runtime states and applies transitions when required. | Stage descriptors execute in fixed order: Plan -> Run -> Validate -> Review -> RequireApproval -> Merge. |
| `AttachSessionAsync` | Restores an existing session from in-memory store or persisted snapshot. | Rehydrates snapshot, tracks attached session, and emits `SessionAttached`. |
| `DetachSessionAsync` | Clears currently attached session context. | Returns `true` once when a session is detached, then `false` for repeated detach calls. |

## State Transition Model

Runtime transitions are strict and linear:

`Created -> Editing -> Validating -> AwaitingApproval -> Approved -> Merged`

Transition attempts that skip or reorder this sequence are rejected.

## Persisted Session Snapshots

Each session mutation persists a JSON snapshot under the repository root:

`.wip/sessions/{sessionId}/session-state.json`

Snapshot payload fields:

- `SessionId`
- `WorkflowId`
- `State`
- `RepositoryPath`
- `WorktreePath`
- `UpdatedAtUtc`

Persistence occurs when a session starts and after every successful transition.

## Attach and Detach Flow

1. `AttachSessionAsync(repositoryPath, sessionId)` resolves from in-memory store first.
2. If not in memory, runtime loads `.wip/sessions/{sessionId}/session-state.json`.
3. Restored snapshot is saved into the active store and `SessionAttached` is emitted.
4. `DetachSessionAsync()` clears the attached session and emits `SessionDetached`.
5. A second detach without re-attach returns `false` and does not emit additional detach state mutation.

## Workflow Stage Progression

`RunWorkflowAsync` uses a fixed stage map and session-state mapping:

| Stage | Session state after stage | Transition applied |
|---|---|---|
| Plan | Editing | Yes (Created -> Editing) |
| Run | Editing | No (state already Editing) |
| Validate | Validating | Yes |
| Review | AwaitingApproval | Yes |
| RequireApproval | Approved | Yes |
| Merge | Merged | Yes |

Contributors changing workflow stage order or state mapping must update this document and runtime behavior tests in tests/Wip.Runtime.Tests.