# WP-3.7 — agent safety tests (partial)

**Status:** in progress 2026-11-05 (tool-level abuse cases covered; prompt-injection and
self-approval cases pending).

## Covered

- **Worktree escape/traversal writes**: `../escape.txt`, absolute paths and `.git/config` are
  rejected by `WorktreePathGuard`; the agent E2E asserts the error observation and that no file was
  written outside the worktree.
- **Injection-driven deletion**: an agent tool call deleting `.git/config` is blocked and the file
  remains intact.
- **Permission denial**: plugin-origin tools are denied before execution (invoker tests), including
  a denied model-provider invocation.
- **Tool loop / budgets**: executor tests cover max steps, token budget, consecutive tool-failure
  limit, loop timeout and caller cancellation.
- **Oversized observations**: the invoker truncates observations with a marker.
- **Model error**: the executor converts provider errors into a deterministic `Failed` result.

## Remaining

- Prompt-injection fixtures (tool-returned instructions), self-approval/merge attempt through the
  agent tools, exfiltration via shell/search, and running these in a dedicated Agent/Security job.