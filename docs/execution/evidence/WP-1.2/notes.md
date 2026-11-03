# WP-1.2 — Worker host + host client (hardened; G2 not ready)

**Status:** implementation hardened; **G2 NOT READY** (C1 OS containment still open).
**Date:** 2026-11-03

## Delivered

- `Wip.Sandbox.Protocol`: versioned envelopes, DTOs, `IWorkerStrategy`, error codes.
- `Wip.Sandbox.Worker`: handshake gate, 1.x negotiation, worker-computed SHA-256 source hash, curated reference set + deny-list, bounded 1 MiB byte reader, Roslyn compile, per-strategy collectible ALC, stdout suppression, response cap, cache cap 64, fixed errors.
- `Wip.Sandbox.WorkerClient`: **clears the inherited environment** and supplies only DOTNET_CLI_HOME/TMP/TEMP/TMPDIR/DOTNET_ROOT; startup/compile/move timeouts; process-tree kill; Windows Job Object with kill-on-close, 256 MiB process-memory cap and 16-process cap; Linux prlimit launcher with CPU/process/virtual-memory bounds.
- 16 worker/client tests; architecture rules for protocol/worker/client.

## Verification

- Worker tests: **16/16 passed**.
- Architecture tests: 3/3 (40 WIP projects); solution build 0 errors; coverage 40/40.
- Offline gate locally: 16 projects / 27 waivers, exit 0.

## Review status

Independent review: **NOT READY FOR G2** ([review](../WP-1.1/security-review-2026-11-03.md)).
Fixed: H2 handshake, H3 hash/cache, H4 ALC, H6 response cap, H7 bounded reader, H8 errors, C2 environment (via client), H5 Windows timeouts/kill/Job Object.
Still open:
- **C1:** no OS file/network/native containment (AppContainer/namespaces/seccomp/landlock). Job Object is a resource cap, not an isolation boundary.
- **H1/TM-13:** P/Invoke is blocked by deny-list/curated refs but not by OS policy.
- **Linux limits:** Job Object is Windows-only; prlimit/cgroup adapter remains WP-1.4.
- Curated reference assemblies (M5) still use TPA filtered by name.

## Gate

G2 **not passed**. WP-1.3 production routing remains blocked. Next: WP-1.4 Linux limits and a G2 decision on C1.
