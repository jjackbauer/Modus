# WP-3.2 — permission-aware tool invoker

**Status:** done 2026-11-05.

## Delivered

- `IToolInvoker` + `ToolInvocationOptions` (`Wip.Abstractions.Tools`): tool calls arrive as a name
  (`ToolCall.Name`) plus JSON arguments; the invoker resolves the capability tool, enforces
  permissions, bounds execution, truncates observations and returns deterministic `ToolResult`s.
- `RuntimeToolInvoker` (`Wip.Runtime.Runtime`):
  - name → tool descriptor map (tool capability ids);
  - JSON arguments deserialized to the descriptor's request type (`JsonSerializerDefaults.Web`,
    case-insensitive);
  - `PermissionGuard` before resolution/execution (plugin tools need grants);
  - per-tool timeout via a linked token source (default 30 s);
  - observation truncation to `MaxObservationCharacters` (default 8,000) with a marker;
  - deterministic errors: `unknown-tool`, `invalid-arguments`, `timeout`, `permission-denied`,
    `tool-error`, `cancelled`; caller cancellation propagates;
  - best-effort JSON artifact recording of call + observation through `IArtifactStore`.
- Tests (8): success JSON observation, unknown tool, invalid arguments, timeout, throwing tool,
  permission denied, truncation, caller cancellation.

## Remaining

None for WP-3.2. WP-3.3 consumes the invoker from the agent loop.