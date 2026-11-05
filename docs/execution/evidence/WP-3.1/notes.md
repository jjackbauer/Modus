# WP-3.1 — chat and tool contracts (increment 1)

**Status:** in progress 2026-11-05. Contracts + tests are in; the DeepSeek `IChatModel` adapter
(SSE streaming) follows.

## Delivered

`Wip.Abstractions.Chat`:

- `ChatRole` (System, User, Assistant, Tool) and `ChatMessage` with `System/User/Assistant/Tool`
  factories; assistant messages carry `ToolCalls`, tool messages carry `ToolCallId`.
- `ToolDefinition` (name, description, JSON-schema parameters), `ToolCall` (id, name, JSON
  arguments), `ToolResult` with `Success`/`Error` factories.
- `ChatRequest` (model, messages, tools, max tokens, temperature, correlation id) with validation
  and defensive snapshots; `ChatResponse` (message, model, provider, usage, correlation);
  `StreamingChunk` (delta, final flag, optional tool call, usage).
- `IChatModel` with `CompleteAsync` and `StreamAsync`; `IModelProvider<,>` remains for backward
  compatibility.

## Tests

`Wip.Abstractions.Tests.Contracts.ChatContractsTests` (11): role factories and tool linkage; JSON
schema/arguments cloned so disposing the caller's document is safe; result factories; request
validation (missing model, no messages, invalid temperature/max tokens); request immutability after
construction; response/chunk exposure.

`Wip.Abstractions.Tests` total: 55 (all passing).

## Remaining

- DeepSeek `IChatModel` adapter (non-streaming completion + SSE streaming) with mock tests.
- Then WP-3.2 tool invoker, WP-3.3 executor, WP-3.4 memory, WP-3.5 streaming wiring, WP-3.6 runtime
  + write-capable agent, WP-3.7 safety tests.