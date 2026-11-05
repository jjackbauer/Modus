# WP-3.6 — runtime agent entry point, write-capable repository tools and real-model sample agent

**Status:** complete 2026-11-05 (local verification; authoritative Tests CI confirmation on the
commit that carries this file). The real-model, write-capable sample agent is proven at the HTTP
boundary, and the opt-in live test is gated by `DEEPSEEK_API_KEY`.

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

## Increment 2 — AgentExecutorTool capability (2026-11-05)

- `AgentExecutorTool` (`tool.agent.run`) + `AddAgentExecutorTool()`: a workflow-facing capability that runs the agent loop with the host's `IChatModel` (required) and, by default, `RuntimeToolInvoker`/`AgentExecutor` over the builder's tools; returns a typed `AgentExecutorToolResult` (status, final answer, steps, error).
- Tests (2): a registered chat model yields a Completed result; a missing chat model fails with a deterministic error.
- `Wip.Runtime.Tests` total 123.

## Remaining

- None for WP-3.6.

## Increment 3 — real-model sample agent at the HTTP boundary (2026-11-05)

- `WIP/Wip.Runtime.Tests/Repo/AgentRealModelHttpBoundaryE2ETests.cs`: a local
  `HttpListener`-based fake DeepSeek server (3 responses: search tool call, write tool call, final
  answer) drives the real `DeepSeekChatModel`, `RuntimeToolInvoker`, `AgentExecutor`,
  `RunAgentAsync` and `WorktreePathGuard` end to end.
- Asserts: the repository question is answered through the real search tool (`needle` in the tool
  result); the agent writes `docs/agent-note.md` inside the isolated worktree with the exact content
  and no file is created outside it; agent memory records the task and final answer; the persisted
  session-state telemetry exists; token usage is recorded; the session is neither `Merged` nor
  `Approved` and no merge artifact exists; the fake server served exactly 3 requests.
- Control: commenting out the tool's `File.WriteAllText` makes the E2E fail, so the edit assertion
  depends on the real write tool.
- `WIP/Wip.Runtime.Tests/Runtime/DeepSeekChatModelLiveTests.cs`: opt-in live test. With
  `DEEPSEEK_API_KEY` unset it returns immediately (no network, no failure, offline-gate safe); with
  the key set it calls the real DeepSeek endpoint and asserts a non-empty `deepseek-chat` answer.
- `Wip.Runtime.Tests` total 132 → 134; the waiver expected total is updated.