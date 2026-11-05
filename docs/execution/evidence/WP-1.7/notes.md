# WP-1.7 — permission model (increment 1)

**Status:** in progress 2026-11-05. Core model and dispatcher enforcement are in; gateway, plugin
load, model invocation and manifest surfacing follow.

## Delivered

- **Catalog** (`Wip.Abstractions.Permissions.PermissionCatalog`): `workspace.read`, `workspace.write`,
  `artifact.read`, `artifact.write`, `model.invoke`, `network`, `process`, `plugin.load`.
- **Model**: `PermissionRequest`, `PermissionDecision`, `PermissionMode` (`Enforce` / `Audit`),
  `IPermissionEvaluator`, `PermissionDeniedException`.
- **Default evaluator** (`Wip.Runtime.Runtime.PermissionEvaluator`): built-in capabilities (no
  plugin origin) are trusted and allowed; plugin capabilities need a grant — `*`, the bare
  permission, or `pluginId:permission`. `Enforce` denies with a reason; `Audit` allows but marks
  `Audited` and reports to an optional audit sink.
- **Enforcement at the stage dispatcher**: before invoking any capability the dispatcher evaluates
  every declared `RequiredPermissions` entry; a denial throws `PermissionDeniedException` before
  execution or artifact persistence. A provider without a registered evaluator falls back to the
  secure default (built-ins allowed, plugins denied).

## Tests

`Wip.Runtime.Tests`:
- `PermissionEvaluatorTests` — built-in allowed, plugin denied, wildcard grant, audit-mode allowed +
  audited + reported.
- `WorkflowStageCapabilityDispatcherTests` — plugin capability without grant is denied (exception
  names the permission); audit mode runs the capability and records the audited decision; a granted
  plugin capability runs.

Focused run: 7/7.

## Remaining

- Enforce `process`/`network` at `RuntimeToolGateway` and `model.invoke` at model-provider invocation.
- Enforce `plugin.load` in the Modus plugin bridge (with granted/denied surfaced in the manifest).
- Wire the audit sink to session events/diagnostics so audit decisions are observable.
- Host-level configuration surface (`PermissionMode` + grants) and docs.