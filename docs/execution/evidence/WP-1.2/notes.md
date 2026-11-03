# WP-1.2 — Worker host (hardened prototype; G2 not ready)

**Status:** implementation hardened after independent review; **G2 NOT READY**.
**Date:** 2026-11-03

## Delivered

- Protocol contracts and JSON-lines worker: handshake/ping/compile/chooseMove/shutdown.
- Handshake gate; 1.x negotiation; worker-computed SHA-256 source hash with mismatch rejection.
- Curated reference set (System.Runtime/Console/Collections/Linq/CoreLib) and a source deny-list for DllImport/LibraryImport/extern/unsafe/System.IO/System.Net/System.Diagnostics/System.Reflection/Runtime.Loader/Environment/AppDomain/Process.
- Bounded byte line reader (1 MiB) instead of unbounded ReadLine.
- Per-strategy collectible AssemblyLoadContext, unload on replacement/shutdown, cache cap 64.
- Model stdout/stderr suppressed during chooseMove; response size cap; fixed error codes/messages.
- 9 protocol/process tests, smoke test, architecture rules for sandbox protocol/worker.

## Verification

- Worker tests: **9/9 passed**.
- Architecture tests: 3/3 passed (39 WIP projects).
- Offline gate locally: 16 projects / 27 waivers, exit 0.
- Build 0 errors; coverage 39/39.

## Review status

Independent review verdict: **NOT READY FOR G2** ([review](../WP-1.1/security-review-2026-11-03.md)).
Fixed after review: H2 handshake, H3 hash/cache, H4 ALC, H6 output cap, H8 errors; H1 mitigated at compile time but not OS-enforced; H7 bounded reader fixed.

Still open:
- **C1:** no OS containment (AppContainer/namespaces/seccomp/landlock); Job Object/prlimit not implemented.
- **C2:** worker inherits parent environment; minimal-allowlist launcher not implemented.
- **H1/TM-13:** P/Invoke blocked by deny-list/curated refs, but reflection/native paths are not an OS boundary.
- **H5:** no host timeouts, process-tree kill, memory/process caps (WP-1.4).

## Gate

G2 is **not passed**. WP-1.3 production routing must not start. WP-1.4 is next.
