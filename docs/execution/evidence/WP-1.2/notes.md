# WP-1.2 — Worker host (hardened prototype; G2 not ready)

**Status:** implementation hardened; **independent review verdict: NOT READY FOR G2**.
**Date:** 2026-11-03
**Projects:** WIP/Wip.Sandbox.Protocol, WIP/Wip.Sandbox.Worker, tests/Wip.Sandbox.Worker.Tests

## Delivered

- Protocol contracts and JSON-lines worker: handshake/ping/compile/chooseMove/shutdown.
- Handshake gate; 1.x negotiation; worker-computed SHA-256 source hash with mismatch rejection.
- Roslyn compile; per-strategy collectible AssemblyLoadContext; unload on replacement/shutdown; cache cap 64.
- Model stdout/stderr suppressed during chooseMove; response size cap; fixed error codes/messages; bounded frame byte check.
- 7 protocol/process tests; smoke test; architecture rules for sandbox protocol/worker.

## Verification

- Worker tests: **7/7 passed**.
- Architecture tests: 3/3 passed (39 WIP projects).
- Offline gate locally: 16 projects / 27 waivers, exit 0.
- `dotnet build Wip.slnx` exit 0; coverage 39/39.

## Review verdict (2026-11-03)

**NOT READY FOR G2.** See [security review](../WP-1.1/security-review-2026-11-03.md).

Open blockers:
- **C1:** no OS containment (AppContainer/namespaces/seccomp/landlock); Job Object/prlimit do not block file/network/native.
- **C2:** worker inherits parent environment/secrets; minimal allowlist not implemented.
- **H1/TM-13:** P/Invoke/native calls permitted.
- **H5:** no host timeouts/process-tree kill/memory cap yet (WP-1.4).
- **H7:** inbound line is fully read before the byte cap is checked.
- **M5:** broad TPA references, not curated reference assemblies.

## Gate

G2 is **not passed**. WP-1.3 production routing must not start. WP-1.4 resource limits and OS-containment evidence are the next work.
