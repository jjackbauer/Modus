# WIP 1.0 — release notes (draft; not released)

**Status:** draft for the G5 decision. **WIP 1.0 is not released.** No package has been published,
signed, or deployed. The release decision is the human G5 gate.

## What WIP 1.0 is

A modular-monolith plugin framework with a session-based agent runtime:

- strict module boundaries, host-owned DI, a deterministic plugin startup pipeline;
- a plugin assembly-load context that isolates dependencies (collectible, per-plugin);
- an agent runtime over a provider-neutral chat contract, with a DeepSeek adapter (completion and
  SSE streaming), a tool invoker with timeouts/truncation/permission gates, an executor with budgets
  and cancellation, and artifact-backed session memory;
- write-capable repository tools confined to the session worktree, with no auto-merge and an
  explicit validate → review → approve → merge boundary;
- a permission system (catalog, Enforce/Audit, dispatcher/tool/plugin-load/model-invoke enforcement);
- a sandbox worker with resource limits and hostile-input tests (Linux verified; Windows OS
  containment is **not** claimed);
- privacy controls: secret redaction in agent memory, data classification, retention/purge.

## Included since the baseline

| Phase | Delivered |
|---|---|
| 1 | worker isolation, resource limits, hostile tests, ScriptHost mode flag, permissions + G3 |
| 2 | host-managed DI, no static pool, declarative workflow plans/executor, explicit stage maps, Bones definition, shell decoupling, collectible plugin ALC (G4) |
| 3 | chat/tool contracts, tool invoker, agent executor + streaming, memory, DeepSeek `IChatModel` adapter (SSE), write-capable repo tools, real-model HTTP-boundary sample agent, safety suite, shell `agent` progress |
| 4 | legacy Modus suite reconciled and blocking again, operator guide, truthful test-status page |
| 5 | privacy/retention (WP-5.2), internal + adversarial security review packet (WP-5.5; external sign-off open) |
| 6 | this release-engineering pack (packaging dry-run, signing plan, compatibility/upgrade, rollback, post-release verification) |

## Compatibility and upgrade

- .NET 10 SDK; Windows 10/11 and Ubuntu builds are green in CI.
- No previous released WIP version exists; the baseline tag `wip-baseline-2026-11-02` is the
  comparison point. See `docs/release/compatibility-and-upgrade.md`.
- Plugin contracts are versioned; old manifests are validated deterministically (see
  `Wip.Modus` manifest validation and compatibility tests).

## Known limits (must appear in any release communication)

1. **Windows OS containment (C1) is not implemented.** The sandbox fails closed but is not OS-level
   containment; the G2 status is conditional/trusted-only.
2. **External/independent security sign-off has not been obtained.** The internal WP-5.5 packet is
   evidence for that review, not an approval.
3. The opt-in live DeepSeek test requires `DEEPSEEK_API_KEY`; the keyed path is not verified in the
   offline gate.
4. Retention/purge is operator-driven (no background scheduler).
5. Secret redaction is pattern-based; unknown credential shapes need added patterns.
6. The full legacy Modus suite is blocking again and green, but its Windows-only test surface is
   not exercised on Ubuntu.

## Verification at this draft

- Tests CI `42059745946` on `9dc8a56`: all six jobs success (offline gate at Runtime 146 / Shell 34,
  architecture 4/4, both security jobs).
- Modus CI `42059745863` on `9dc8a56`: success with the blocking legacy suite.
- Local dry-run publish and checksums: `docs/release/packaging-and-signing.md`.

## Decision

Proceed to G5 only when the G2 re-review (2026-12-03, or explicit human sign-off), the external
security sign-off, and the release-engineering checks in `docs/release/post-release-verification.md`
have been decided by the human owner. Until then: **no release claim, no publish, no sign, no
deploy.**
