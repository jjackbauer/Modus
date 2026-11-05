# WP-3.5 — streaming (adapter delivered)

**Status:** in progress 2026-11-05. Agent streaming execution, the DeepSeek `IChatModel` adapter and
host registration are in; shell/status progress rendering remains.

## Delivered

- Contracts (`Wip.Abstractions.Agents.AgentStreamingContracts`): `AgentStreamEventKind`
  (Delta, ToolCall, ToolResult, Completed, Failed), `AgentStreamEvent`, `IStreamingAgentExecutor`.
- `AgentExecutor.ExecuteStreamingAsync`: streams model deltas as they arrive, surfaces tool-call
  chunks, invokes tools and reports each `ToolResult`, then completes with the final message; it
  applies the same step/token/failure/timeout limits and yields `Failed` events with the
  corresponding `AgentExecutionStatus`.
- Tests (3): text deltas then Completed; tool-call stream → ToolResult → Completed (invoker called);
  model error → Failed event.
- `DeepSeekChatModel : IChatModel` (`Wip.Runtime/Runtime/DeepSeekChatModel.cs`): non-streaming
  completion and SSE streaming. Vendor payload types are private; the public surface is
  `ChatRequest`/`ChatResponse`/`StreamingChunk`. It maps system/user/assistant/tool messages,
  assistant `tool_calls`, tool definitions, `max_tokens`, `temperature`, bearer auth from
  `env:VARIABLE`, bounded transient retries, a linked timeout, `[DONE]`, usage, and assembles split
  tool-call argument deltas.
- `AddDeepSeekChatModel(...)` registers options, `HttpClient` and `IChatModel`;
  `WipShellHostFactory` now registers the adapter whenever the DeepSeek provider is configured.
- Tests (7, mock `HttpMessageHandler`): request mapping with tools, tool-call response parsing and
  tool-message mapping, transient retry then deterministic failure, cancellation, SSE deltas +
  usage, split tool-call deltas, environment key resolution. `Wip.Runtime.Tests` total 123 → 130;
  waiver register expected total updated.

## Remaining

- Shell/status progress rendering of stream events (a shell agent command consuming
  `IStreamingAgentExecutor`).
- A real-model, write-capable sample agent E2E over the DeepSeek HTTP boundary (WP-3.6) and the
  remaining safety fixtures (WP-3.7).