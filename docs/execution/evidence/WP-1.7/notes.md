# WP-1.7 — permission model (done)

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

## Increment 2 — gateway, plugin load, manifests, host wiring (2026-11-05)

- **Tool gateway**: `RuntimeToolGateway.InvokeAsync` evaluates the tool descriptor through the shared
  `PermissionGuard` before resolving or executing the tool; denials never produce artifacts. Tests:
  plugin-origin tool denied without a grant, granted plugin tool executes, and a denial leaves the
  artifact store empty.
- **Plugin load**: `ModusWipBridge` accepts an optional `IPermissionEvaluator`. With one, loading a
  plugin requires `plugin.load` plus every declared permission for its plugin origin
  (`pluginId:permission` grants); a denial is skipped with a `[permission]` diagnostic, audit mode
  logs the would-be denial and loads. Without an evaluator the bridge keeps its historical
  load-everything behaviour.
- **Manifest**: `PluginManifestEntry.GrantedPermissions` surfaces the permissions that were granted
  (or, with no evaluator, the declared permissions).
- **Host wiring**: `services.AddWipPermissions(mode, grants, auditSink)` registers the evaluator with
  the desired mode and grants.
- **Moved** the default `PermissionEvaluator` to `Wip.Abstractions.Permissions` so hosts and the
  plugin bridge can share it without depending on the runtime.
- Tests: gateway deny/grant, bridge deny/audit/grant (manifest grants), evaluator unit tests.

## Increment 3 — model invocation (2026-11-05)

- `PermissionGuardedModelProvider<TRequest,TResult>` wraps a model provider and evaluates
  `model.invoke` before calling the inner provider. `services.AddPermissionGuardedModelProvider<...>()
  ` registers the inner provider and exposes the guarded wrapper; a plugin origin makes the call
  grant-dependent, a null origin keeps built-ins allowed.
- Tests: built-in forwarded; plugin provider denied before invocation; audit mode invokes and
  records the audited decision.

## G3 status

The permission model is complete for G3: catalog, evaluator modes, grants, enforcement at all four
required points, manifest surfacing, and 15 permission tests. The external security review (WP-5.5)
remains a later activity, and the model guard is opt-in host wiring.

## Remaining

- Enforce `process`/`network` at `RuntimeToolGateway` and `model.invoke` at model-provider invocation.
- Enforce `plugin.load` in the Modus plugin bridge (with granted/denied surfaced in the manifest).
- Wire the audit sink to session events/diagnostics so audit decisions are observable.
- Host-level configuration surface (`PermissionMode` + grants) and docs.