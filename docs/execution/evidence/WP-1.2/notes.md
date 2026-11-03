# WP-1.2 — Worker host (implementation)

**Status:** implementation complete; **G2 not passed**.
**Date:** 2026-11-02
**Projects:** WIP/Wip.Sandbox.Protocol, WIP/Wip.Sandbox.Worker, tests/Wip.Sandbox.Worker.Tests

## Delivered

- Versioned protocol contracts (`SandboxProtocol`, request/response envelopes, `WorkerStrategyState`, `IWorkerStrategy`, error codes).
- `Wip.Sandbox.Worker` console host: UTF-8 JSON-lines stdio, BOM tolerance, handshake/ping/compile/chooseMove/shutdown, correlation IDs, bounded frames, deterministic error codes.
- Roslyn compile in the worker; model assembly loaded only in the worker; cache by strategyId/sourceHash; no host DI, no host/plugin assembly resolution.
- Protocol tests (3) and a process-level smoke test.

## Acceptance

| Criterion | Status |
|---|---|
| Worker compiles and executes a sample strategy | **PASS** — tests + smoke |
| Malformed input returns deterministic errors | **PASS** — InvalidMessage |
| Protocol tests pass | **PASS** — 3/3 |
| Host never loads a model-authored assembly | **PASS for worker; host routing is WP-1.3** |

## Evidence

- [worker-smoke.txt](worker-smoke.txt) — handshake, ping, compile, chooseMove=0, shutdown.
- [worker-tests.txt](worker-tests.txt) — worker tests 3/3; architecture tests 3/3; solution build 0 errors; coverage 39/39.
- Wip.Sandbox.Worker.Tests.trx / Wip.Architecture.Tests.post-phase1.trx.

## Residual / G2 blockers

- **No resource limits or kill yet** (WP-1.4): infinite loop/memory/output can still exhaust the worker.
- **P/Invoke/native calls are not blocked** by the current TPA reference set (TM-13 open; documented in the threat model).
- **Contract surface is broad:** model source can reference BCL/TPA, not just the protocol contract; G2 must accept or restrict.
- No worker restart/health policy; no feature flag (WP-1.6); no host routing (WP-1.3).
- The security review is pending; this note does not self-approve G2.
