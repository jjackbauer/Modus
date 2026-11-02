# ADR-IMPL-03 — Permission model and enforcement

- **Status:** Proposed (G3 decision required)
- **Date:** 2026-11-02
- **Owner:** security reviewer + runtime lead
- **Reversal cost:** High

## Context

Capabilities and plugins declare permissions, but nothing enforces them at execution time. The plan requires a catalog, an evaluator, audit/enforce modes, and manifest visibility.

## Decision

- Catalog: `workspace.read`, `workspace.write`, `artifact.read`, `artifact.write`, `model.invoke`, `network`, `process`, `plugin.load`.
- Add `IPermissionEvaluator` and `PermissionContext`; enforce at the dispatcher, `RuntimeToolGateway`, plugin load, and model-provider invocation.
- Modes: `Audit` (log would-be denials) and `Enforce` (block). Start in `Audit` for at least one Bones and shell E2E iteration.
- Built-in capabilities receive explicit grants; unresolved grants fail closed in `Enforce`.
- Grants and denials are surfaced in run manifests and audit records.

## Consequences

- Positive: least privilege becomes executable, not metadata.
- Negative: migration cost; built-ins may need grant fixes; model calls/plugins can fail closed.

## Reversal triggers

- Built-ins cannot be made to work without broad grants.
- Audit shows unexplained denials after one iteration.
- OS-level isolation already prevents a class of actions (then the catalog can be reduced, not bypassed).

## Open questions

- Default grants for `model.invoke` and `artifact.*`. Owner: security reviewer.
- Audit-log retention and export. Owner: platform engineer.
