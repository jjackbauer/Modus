# WP-3.3 — agent executor loop (increment 1)

**Status:** in progress 2026-11-05 (loop + budgets done; streaming integration and runtime wiring follow).

## Delivered

- Contracts (`Wip.Abstractions.Agents`): `AgentLoopOptions` (max steps, max total tokens, timeout,
  max consecutive tool failures), `AgentExecutionRequest`, `AgentExecutionStatus`
  (Completed, MaxStepsReached, TokenBudgetExhausted, ToolFailureLimitReached, TimedOut, Failed),
  `AgentStep`, `AgentExecutionResult`, `IAgentExecutor`.
- `AgentExecutor` (`Wip.Runtime.Runtime`): builds system/initial/user messages, then loops
  model turn → tool calls → tool observations (as `ChatRole.Tool` messages) until a final answer or a
  stop condition. It accumulates usage, enforces the token budget after each tool batch, counts
  consecutive tool failures, applies the loop timeout, converts model errors to a `Failed` result,
  and propagates caller cancellation.
- Tests (7): immediate final answer, tool call then final answer with observation feedback,
  max-steps stop, repeated tool failures, token budget, model error, caller cancellation.

## Remaining

- Streaming execution (WP-3.5) and runtime `RunAgentAsync` + write-capable sample agent (WP-3.6).
- Memory (WP-3.4).