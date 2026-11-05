# WP-3.4 — agent memory (done)

**Status:** done 2026-11-05.

## Delivered

- Contracts (`Wip.Abstractions.Agents.AgentMemoryContracts`): `AgentMemoryOptions` (retained
  messages), `AgentMemorySnapshot`, `IAgentMemory` (`LoadAsync`, `AppendAsync`, `SummarizeAsync`).
- `ArtifactAgentMemory` (`Wip.Runtime.Runtime`): one `agent-memory.json` artifact per session
  (producer `ArtifactAgentMemory`); appends load the latest artifact, append the message, retain the
  tail (`MaxMessages`) and rewrite the artifact; load finds the newest memory artifact; summarize
  returns the last N messages with an omitted-count marker. Sessions are isolated by artifact
  session id.
- Runtime wiring: `RunAgentAsync` loads the session memory, prepends it to the agent's initial
  messages, and appends the user task plus the final assistant message after the run.

## Tests

5 memory tests (survives across instances, session isolation, retention tail, summarize marker,
artifact inspectable) plus a runtime E2E proving a second agent run sees the first run's task and
answer (4 memory messages). `Wip.Runtime.Tests` total 121.