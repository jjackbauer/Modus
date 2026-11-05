# WP-3.5 — streaming (complete)

**Status:** complete 2026-11-05 (local verification; authoritative Tests CI confirmation on the
commit that carries this file). The adapter, runtime streaming entry point and shell progress
rendering are in.

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
- `WipRuntimeOrchestrator.RunAgentStreamingAsync`: streaming runtime entry point that loads the
  session, resolves `IChatModel`/`IToolInvoker`, composes the streaming executor by default, merges
  session memory, yields the adapter events and appends the task + final answer to memory once the
  stream completes. Tests (2): text deltas + Completed + memory appended; tool-call chunk → real
  `tool.repo.read_file` result → final answer.
- Shell progress rendering: new session command `agent "<task>"` runs the streaming loop, prints
  model deltas as they arrive, shows `[tool-call]`/`[tool-result]` (observations truncated at 200
  characters), and prints the final status/answer. The no-session guidance and the authoritative
  command semantics/help text include `agent`. Test: a scripted streaming model produces
  `hello` before `Agent status: Completed`; removing the delta rendering makes it fail (control).
- `Wip.Shell.Tests` total 32 → 33; `Wip.Runtime.Tests` total 130 → 132.

## Remaining

- None for WP-3.5. The next Phase 3 work is WP-3.6 (real-model write-capable sample agent) and the
  WP-3.7 safety remainder.