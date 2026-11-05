# WP-3.5 — streaming (execution core done)

**Status:** in progress 2026-11-05. Agent streaming execution and tests are in; a vendor SSE adapter
and shell progress display remain.

## Delivered

- Contracts (`Wip.Abstractions.Agents.AgentStreamingContracts`): `AgentStreamEventKind`
  (Delta, ToolCall, ToolResult, Completed, Failed), `AgentStreamEvent`, `IStreamingAgentExecutor`.
- `AgentExecutor.ExecuteStreamingAsync`: streams model deltas as they arrive, surfaces tool-call
  chunks, invokes tools and reports each `ToolResult`, then completes with the final message; it
  applies the same step/token/failure/timeout limits and yields `Failed` events with the
  corresponding `AgentExecutionStatus`.
- Tests (3): text deltas then Completed; tool-call stream → ToolResult → Completed (invoker called);
  model error → Failed event.

## Remaining

- DeepSeek SSE adapter for `IChatModel.StreamAsync` (the contract is in place; no vendor adapter yet).
- Shell/status progress rendering of stream events.