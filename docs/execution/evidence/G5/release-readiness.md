# G5 release-readiness — **not requested; no release claim**

**Date:** 2026-11-05
**Branch / HEAD:** `WIP` @ `9dc8a56` (last fully green code HEAD; records commits follow)
**Status:** **WIP 1.0 is not release-ready.** G5 has not been requested and no release is claimed.
The human G5 decision is the only path to a release; **no publish/sign/deploy has been performed.**

## Ready and verified

| Area | State |
|---|---|
| Phase 0 (baseline, decisions, G0, ADR) | complete |
| Phase 1 isolation (WP-1.1..1.7) | worker process, resource limits, hostile tests, ScriptHost flag, permissions + G3 |
| Phase 2 runtime seams (WP-2.1..2.7) + G4 | host-managed DI, no static pool, declarative plans, explicit maps, Bones definition, shell decoupling, collectible plugin ALC |
| Phase 3 agent runtime (WP-3.1..3.7) | chat/tool contracts, invoker, executor + streaming, memory, DeepSeek `IChatModel` adapter (SSE), write-capable repo tools, real-model HTTP-boundary sample agent, safety suite, shell `agent` progress |
| Phase 4 CI truth | legacy Modus suite reconciled and **blocking** and green in Modus CI; operator guide; truthful test-status page |
| Phase 5 privacy/security | redaction + retention/purge (WP-5.2); internal + adversarial security packet (WP-5.5; external sign-off open) |
| Phase 6 release engineering (local draft) | release notes, packaging/signing plan, compatibility/upgrade, rollback, post-release verification; local publish dry-run + checksums |
| Gates | Tests CI `42059745946` on `9dc8a56` all six jobs (Runtime 146 / Shell 34; both security jobs; architecture 4/4 at 43 projects); Modus CI `42059745863` success with the blocking legacy suite |

## Blockers to a WIP 1.0 release (G5)

1. **G2 remains conditional/trusted-only** and **Windows OS containment (C1) is unclaimed**. The
   re-review is scheduled for 2026-12-03 (`docs/execution/evidence/G2/re-review-packet-2026-12-03.md`);
   no upgrade without human sign-off.
2. **External/independent security sign-off is not obtained.** The WP-5.5 packet is internal +
   adversarial evidence, not an approval.
3. **Phase 6 verification items not executed:** a 24-hour soak, a 2,000-game benchmark, and a
   formal three-consecutive-green-run release freeze. The local dry-run and rollback controls are
   performed; the stress/soak evidence is not.
4. **Signing credentials/publication authority** are human-owned and not available; no signature or
   publication exists.
5. **The opt-in live DeepSeek path is unverified** without an API key.

## Exact release steps (not executed)

1. Human G5 decision after reviewing this packet, the G2 re-review, and the external security
   sign-off.
2. Freeze the release commit; re-run Tests CI (all six jobs) and Modus CI on it; record run ids.
3. Sign and pack using the owner-supplied credentials; publish host artifacts + NuGet packages.
4. Publish release notes, compatibility/upgrade notes, support matrix and checksums.
5. Run `docs/release/post-release-verification.md`; record results; open follow-ups.

## Known limits

C1 unclaimed; G2 conditional; external sign-off open; shell tool privileged; pattern-based
redaction; operator-driven retention; live-model path unverified without credentials.

## Recommendation

**Do not release.** Complete the G2 re-review and external security sign-off, decide C1, execute the
Phase 6 stress/soak evidence, then bring the human G5 decision. Until then this packet remains
evidence, not a release.
