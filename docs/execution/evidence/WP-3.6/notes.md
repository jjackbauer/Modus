# WP-3.6 — runtime agent entry point and write-capable repository tools (core)

**Status:** in progress 2026-11-05. Write-capable tools, runtime `RunAgentAsync` and an agent E2E
are in; memory-backed and real-model sample flows plus the `AgentExecutorTool` workflow capability
remain.

## Delivered

- **`Wip.Tools.Repo`** (new project, included in `Wip.slnx`; architecture graph is 43 projects):
  - `RepoSearchTool` (`tool.repo.search`), `RepoReadFileTool` (`tool.repo.read_file`),
    `RepoWriteFileTool` (`tool.repo.write_file`), `RepoDeleteFileTool` (`tool.repo.delete_file`),
    `RepoGitStatusTool` (`tool.repo.git_status`).
  - `WorktreePathGuard` resolves every path inside the session worktree: absolute paths, `..`
    segments and `.git` internals are rejected; search skips `.git`/`bin`/`obj`.
  - `AddRepoAgentTools()` registers the five tools with `workspace.read`/`workspace.write` and
    `process` permissions (enforced by the tool invoker).
- **Runtime**: `WipRuntimeOrchestrator.RunAgentAsync(request, builder, services, ct)` loads the
  session, resolves `IChatModel` (required), and composes `RuntimeToolInvoker` + `AgentExecutor`
  unless the host registered its own; the model-facing context is the session worktree.
- **E2E (scripted model, real tools + invoker + executor)**: an agent writes `docs/agent-note.md`
  inside its worktree and completes; a traversal write (`../escape.txt`) is blocked with an error
  observation and no file escape; deleting `.git/config` is blocked and the file stays intact; an
  absolute write is blocked.

## Remaining

- Artifact memory (WP-3.4) and a sample agent that answers a repository question with a
  real/DeepSeek chat model; `AgentExecutorTool` capability so workflows can run the agent loop.