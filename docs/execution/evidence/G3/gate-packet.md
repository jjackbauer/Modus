# G3 gate packet — permission model approved

**Gate:** G3 "permission model approved" (internal engineering gate; the external security review is
WP-5.5 and the release decision is G5).
**Date:** 2026-11-05
**Branch / HEAD:** `WIP`.

## Scope — WP-1.7

| Piece | Delivered |
|---|---|
| Catalog | `workspace.read/write`, `artifact.read/write`, `model.invoke`, `network`, `process`, `plugin.load` (`PermissionCatalog`). |
| Model | `PermissionRequest`, `PermissionDecision`, `PermissionMode` (`Enforce`/`Audit`), `IPermissionEvaluator`, `PermissionDeniedException`, default `PermissionEvaluator`. |
| Grants | Built-in capabilities are trusted; plugin capabilities need `*`, the bare permission, or `pluginId:permission`. |
| Enforcement | (1) workflow stage dispatcher before capability invocation; (2) `RuntimeToolGateway` before tool resolution/execution; (3) `ModusWipBridge` on plugin load (`plugin.load` + declared permissions); (4) `PermissionGuardedModelProvider` around `model.invoke`. |
| Audit | Audit mode allows the action, marks the decision audited, and reports it to the configured sink. |
| Surfacing | `PluginManifestEntry.GrantedPermissions`; plugin load diagnostics `[permission]`. |
| Host wiring | `services.AddWipPermissions(mode, grants, auditSink)` and `AddPermissionGuardedModelProvider<...>()`. |

## Verification

- Permission tests (15): evaluator allow/deny/wildcard/audit; dispatcher deny/audit/grant; gateway
  deny/grant with no artifact on denial; bridge deny/audit/grant with manifest grants; model guard
  built-in/denied/audit.
- Suites: Runtime 87 (1 pre-existing waived), Modus 27 (1 pre-existing waived), ShellHost 47, Shell 32
  (2 pre-existing waived), Bones 396 (2 pre-existing waived).
- No behavior change for built-ins: all suites stayed green with enforcement active.

## Known limits

- The model guard is available and tested but hosts must opt in when they compose plugin-provided
  model providers; built-in providers are allowed by design.
- Windows OS containment (C1) is unaffected and remains unclaimed; G2 stays conditional until the
  2026-12-03 re-review.
- No release claim before G5.

## Decision requested

Approve G3: the permission model is defined, enforced at all required boundaries, auditable, and
covered by allow/deny/audit tests, so Phase 3 can build on it.