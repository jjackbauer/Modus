# GATE: G2 — sandbox mechanism and residual risk

**Status:** **CONDITIONALLY APPROVED (Option 2)** — trusted-only, default-off flag, expires 2026-12-03; C1 remains open.
**Date:** 2026-11-03
**Decision owners:** security reviewer + tech lead (plan §5.2)
**Evidence:** [threat model](../../security/sandbox-threat-model.md), [protocol](../../security/worker-protocol.md), [independent review](../WP-1.1/security-review-2026-11-03.md), [WP-1.2 notes](../WP-1.2/notes.md)

## What is implemented

- Separate `Wip.Sandbox.Worker` process; host never loads model-authored assemblies.
- Handshake gate, 1.x negotiation, version mismatch exit.
- Worker-computed SHA-256 source hash; mismatch rejection; cache cap 64.
- Curated reference set and deny-list for DllImport/LibraryImport/extern/unsafe/File/Network/Process/Reflection/Environment; 16 protocol/hostile tests.
- Bounded 1 MiB byte reader; response cap; stdout suppression; fixed errors.
- Per-strategy collectible ALC; unload on replacement/shutdown.
- Host client clears the inherited environment and applies timeouts, process-tree kill, Windows Job Object (kill-on-close, 256 MiB, 16 processes), and Linux `prlimit` (CPU/process/virtual-memory) where available.
- Infinite-loop timeout/kill and environment-allowlist tests pass.

## Why G2 is not approved automatically

The independent review verdict is **NOT READY FOR G2**:

- **C1:** no OS file/network/native containment. Job Objects/prlimit are resource caps, not AppContainer/namespaces/seccomp.
- **H1/TM-13:** P/Invoke is blocked by source policy/curated refs, but that is not an OS boundary; reflection/native escapes remain possible.
- **Linux:** prlimit is best-effort and not cgroup/seccomp; no network namespace.
- **M5:** curated references are filtered TPA, not the reference-assembly pack.
- The worker/client are not yet wired into Bones production execution (WP-1.3), so a G2 approval here would authorize that.

## Decision options

1. **Fail G2 for production routing.** Keep WP-1.3 blocked; continue with AppContainer/namespaces/seccomp and a hostile OS test suite. Recommended if model-authored code remains untrusted.
2. **Approve a narrowed G2 for trusted-only use.** Permit WP-1.3 only behind a default-off feature flag for trusted/experimental strategies; no untrusted model code; document the residual; expiry/date for stronger isolation. This does not satisfy C1.
3. **Approve full production G2 with C1 accepted as residual risk.** Not recommended; it contradicts the threat model's guarantees.

## Recommendation

Option 1. Do not route production model code until an OS containment layer and hostile tests exist. If the product needs progress, use Option 2 with a written, time-bounded residual and no untrusted strategies.

No G2 approval is recorded here.
