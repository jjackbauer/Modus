# WP-1.6 — ScriptHost mode flag (safe scaffold)

**Status:** in progress; **Worker routing is not implemented** (WP-1.3).
**Date:** 2026-11-03

## Delivered

- `BonesScriptHostMode` = `InProcess` (default) | `Worker`.
- `BonesStrategyScriptHostOptions.Mode` and `TrustedOnly` (default true).
- `BonesHost:ScriptHost:Mode` and `TrustedOnly` bind from configuration/appsettings.
- `Worker` without `TrustedOnly=true` throws; `Worker` with it fails closed with `NotSupportedException` until WP-1.3.
- Two guard tests; DEV-004 records the default-InProcess deviation from the plan.

## Why not “done”

WP-1.6 acceptance says the flag switches modes and default is Worker. The flag exists but `Worker` is intentionally a fail-closed scaffold; there is no WorkerScriptExecutor and no production routing. C1 OS containment remains open.

## Evidence

- `BonesStrategyScriptHost_GivenWorkerModeWithoutTrustedOnly_ExpectedThrows`
- `BonesStrategyScriptHost_GivenWorkerModeTrustedOnly_ExpectedFailsClosed`
- [DEV-004](../deviations/DEV-004-script-host-mode-default.md)
