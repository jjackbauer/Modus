# Rollback and feature-flag procedure (Phase 6)

**Status:** rehearsed locally where possible; no deployment exists to roll back. No release is
claimed.

## Rollback strategy

WIP keeps all state on disk inside the repository (`.wip/sessions`, `.wip/worktrees`,
`.wip/plugins`) and has no database migration. Rollback is therefore: stop the host, restore the
previous artifacts/configuration, start the host again. Sessions created by a newer version remain
readable (records are additive); a release-blocking incompatibility is treated as a no-go at G5.

Target: **under 15 minutes** from decision to a running previous version with no loss of session
evidence.

## Feature flags and kill switches

| Control | Values | Rollback behavior |
|---|---|---|
| `ScriptHost:Mode` | `InProcess` (default), `Worker` | Switch back to `InProcess` + restart; Worker fails closed and never silently falls back |
| Permission mode | `Enforce` (default), `Audit` | `Audit` is diagnostic only; returning to `Enforce` restores denial semantics |
| Plugin lifecycle | `plugins load` / `plugins unload`; collectible ALC | `plugins unload` stops the plugin and unloads its load context |
| Agent/loop | shell `abort`, session detach, executor cancellation | cancellation closes the model stream and stops the loop |
| Worker kill switch | host restart / `ScriptHost:Mode` | no orphan worker should remain (tests assert no worker processes after exit) |

## Rehearsal (local evidence)

- Worker-mode flag: `Wip.Bones.Host.Tests` ScriptHost tests (options binding, unknown mode rejection,
  fail-closed paths, worker execution without model assembly in the host) — 10/10 locally.
- Permission modes: evaluator/dispatcher `Enforce`/`Audit` tests (15 permission tests).
- Process leak: the host E2E asserts no worker processes remain after exit; sandbox suite 18/18.
- No production deployment was rolled back because none exists; this is a dry-run of the controls.

## Procedure (when a deployment exists)

1. Announce and stop new work; keep session artifacts.
2. Stop the host (graceful), confirm no worker/child processes remain.
3. Restore the previous published artifacts and configuration; keep `.wip` state.
4. Start the host, run `retention status`, attach a session and verify `status`/`artifacts`.
5. Record the rollback (time, reason, version, evidence) in the incident record.
6. Open follow-ups; do not re-release until the root cause is fixed and re-gated.
