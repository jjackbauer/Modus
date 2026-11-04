# DEV-004 — ScriptHost mode defaults to InProcess, not Worker

**Status:** active deviation; approved scope is conditional Gate G2 (trusted-only). Re-review at G2/G5.
**Date:** 2026-11-03 (updated 2026-11-04 after WP-1.3/WP-1.6)
**Plan reference:** WP-1.6 (“default Worker”)

## What the plan assumed

WP-1.6 would add `ScriptHost:Mode=Worker|InProcess`, default `Worker`, with `InProcess` restricted to tests/trusted use.

## What is true now

- WP-1.3 worker routing is implemented: in Worker mode `BonesStrategyScriptHost` compiles and selects moves through `SandboxWorkerClient`, the host caches a `WorkerBonesPlayerSlot` bridge, and the model assembly is never loaded in the host. Evidence: `docs/execution/evidence/WP-1.3/worker-coverage.md`.
- WP-1.6 implements the flag end to end: `BonesScriptHostRuntime`, configuration binding, deterministic startup validation, the InProcess warning, Worker opt-in with a fail-closed start, and tests. Evidence: `docs/execution/evidence/WP-1.6/notes.md`.
- The default remains **`InProcess`** deliberately: Gate G2 is conditional (trusted-only, expiry 2026-12-03), Windows C1 is an accepted residual, and release packaging does not yet deploy the worker (WP-6.2).
- `Worker` requires `TrustedOnly=true`; a missing worker, a failed handshake, or `TrustedOnly=false` stops the host at startup. There is no silent in-process fallback.
- C1: Linux verified (unshare + chroot); Windows not contained (Job Object only). No release.

## Impact

- The plan’s “default Worker” is not met. `InProcess` remains the shipped default and logs a warning at startup.
- Execution routing itself is complete: setting `BonesHost:ScriptHost:Mode=Worker` switches the host to the worker once the worker binary is deployed and `ScriptHost:WorkerPath` resolves.

## Reversal

When all of the following hold — full G2 approval, Windows containment fixed or explicitly accepted for release, and release packaging deploying the worker — make `Worker` the default, keep the fail-closed guards, and retire this deviation at G5.