# DEV-004 — ScriptHost mode defaults to InProcess, not Worker

**Status:** proposed for G2/G5 review
**Date:** 2026-11-03
**Plan reference:** WP-1.6 (“default Worker”)

## What the plan assumed

WP-1.6 would add `ScriptHost:Mode=Worker|InProcess`, default `Worker`, with `InProcess` restricted to tests/trusted use.

## What is true

- WP-1.3 (worker routing) is not implemented; `BonesStrategyScriptHost` still compiles/executes in-process.
- Under G2 Option 2 (trusted-only, conditional), the safe default is **`InProcess`**.
- Selecting `Worker` requires `TrustedOnly=true` and currently **fails closed** with `NotSupportedException` until WP-1.3 exists.
- C1 OS containment is still open; WIP 1.0/G5 remain blocked until it closes.

## Impact

- The flag exists and is configurable, but does not yet switch execution to the worker. This is a scaffold, not WP-1.3 completion.
- The plan’s “default Worker” is not met; this deviation is deliberate and safer for the conditional G2.

## Reversal

When WP-1.3 routes execution through `WorkerScriptExecutor` and C1 containment evidence exists, make `Worker` the default, remove the fail-closed guard, and retire this deviation.
