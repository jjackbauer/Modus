# Worker protocol v1.0 (WP-1.1 draft)

**Status:** DRAFT — versioned specification for G2 review.
**Transport:** stdio, UTF-8 JSON Lines (one JSON object per line).
**Version:** 1.0. Unknown major versions fail closed.

## 1. Framing and limits

- One JSON object per line; no embedded raw newlines.
- Max inbound frame: 1 MiB. Max outbound frame: 1 MiB.
- Max stdout/stderr retained per worker: 256 KiB each; excess is drained/discarded and counted.
- Every request has a host-generated correlation ID (UUID string) and a type.
- Every response echoes the correlation ID and carries ok/error.
- JSON property names are camelCase and stable; unknown properties are ignored.
- Protocol version is negotiated in handshake; mismatch returns protocolVersionUnsupported.

## 2. Lifecycle

~~~text
host -> worker: handshake
worker -> host: handshakeResult
host -> worker: compile | ping | shutdown
worker -> host: compileResult | pong | shutdownResult
host -> worker: chooseMove
worker -> host: chooseMoveResult
~~~

The worker must not accept compile/chooseMove before a successful handshake.

## 3. Messages

### handshake (host -> worker)

~~~json
{
  "type": "handshake",
  "id": "6f1c...",
  "protocolVersion": "1.0",
  "workerVersion": "0.1.0",
  "limits": {
    "maxMoveMilliseconds": 2000,
    "maxCompileMilliseconds": 30000,
    "maxMemoryBytes": 268435456,
    "maxOutputBytes": 262144
  }
}
~~~

### handshakeResult (worker -> host)

~~~json
{
  "type": "handshakeResult",
  "id": "6f1c...",
  "ok": true,
  "protocolVersion": "1.0",
  "capabilities": ["compile", "chooseMove", "ping", "shutdown"]
}
~~~

### compile (host -> worker)

~~~json
{
  "type": "compile",
  "id": "b2...",
  "strategyId": "strategy-001",
  "sourceHash": "sha256:...",
  "source": "using ...; public sealed class Strategy { ... }"
}
~~~

### compileResult

~~~json
{
  "type": "compileResult",
  "id": "b2...",
  "ok": true,
  "assemblyHash": "sha256:...",
  "diagnostics": []
}
~~~

Compile failure returns ok=false, error.code=compileError, and deterministic diagnostics (no host paths).

### chooseMove (host -> worker)

~~~json
{
  "type": "chooseMove",
  "id": "c3...",
  "strategyId": "strategy-001",
  "state": {
    "board": ["........", "........"],
    "player": "X",
    "legalMoves": [0, 1, 2],
    "turn": 7
  }
}
~~~

The state DTO is stable and does not expose BonesRoundState internals.

### chooseMoveResult

~~~json
{ "type": "chooseMoveResult", "id": "c3...", "ok": true, "move": 4 }
~~~

A timeout, crash, or invalid move returns ok=false with a deterministic error.

### ping / pong

~~~json
{ "type": "ping", "id": "d4..." }
{ "type": "pong", "id": "d4...", "ok": true }
~~~

### shutdown / shutdownResult

~~~json
{ "type": "shutdown", "id": "e5..." }
{ "type": "shutdownResult", "id": "e5...", "ok": true }
~~~

## 4. Error taxonomy

| Code | Meaning | Host action |
|---|---|---|
| protocolVersionUnsupported | major version mismatch | stop worker; fail closed |
| invalidMessage | malformed/oversized/missing fields | stop/restart; fail move |
| limitExceeded | memory/output/time/process cap | kill tree; restart |
| compileError | source did not compile | return diagnostics; no assembly loaded |
| sourceHashMismatch | worker-computed hash differs from the claimed hash | reject; no assembly loaded |
| timeout | operation exceeded its budget | kill tree; restart |
| resourceExhausted | OS limit hit | kill tree; restart |
| workerCrash | process exited unexpectedly | restart; fail current request |
| internalError | unexpected worker failure | log correlation; restart |

Errors never include host paths, secrets, or stack traces that reveal host internals. The worker also emits `{"type":"error",...}` for frame/protocol failures; clients must tolerate it.

## 5. Timeouts and kill policy

- Startup: 15 s; handshake: 5 s; compile: 30 s; move: 2 s (configurable within bounds).
- A timeout triggers process-tree kill (Job Object/prlimit), temp cleanup, and a deterministic timeout error.
- The host restarts a fresh worker for the next request; no reuse after a violation.

## 6. Security rules

- No host DI, no host assembly resolution, no plugin assemblies.
- No inherited environment secrets; construct a minimal environment.
- No file/network APIs in the worker's approved surface; enforce with tests and OS limits.
- Model assembly is loaded only in the worker ALC; unload/collect after strategy replacement where possible.
- stdout/stderr are protocol/log only; no arbitrary host output.

## 7. Compatibility

- Version 1.0 is the initial contract.
- Additive optional properties are allowed within 1.x.
- Removing/renaming fields or changing semantics requires 2.0.
- The host and worker negotiate in handshake; unsupported versions fail closed.
- Compile/chooseMove before a successful handshake return `invalidMessage`.
- A minor version in the 1.x line is accepted; the worker advertises 1.0.
- Responses exceeding 256 KiB are replaced by a `limitExceeded` error frame.
- Model stdout/stderr are suppressed during `chooseMove`; protocol output stays on the host channel.
- Each strategy is loaded in a collectible `AssemblyLoadContext`; replacement/shutdown unloads it.
- The worker currently inherits the parent environment; the host launcher must supply a minimal allowlist (C2, open).
- P/Invoke/native calls are not blocked by this protocol (TM-13, open).
