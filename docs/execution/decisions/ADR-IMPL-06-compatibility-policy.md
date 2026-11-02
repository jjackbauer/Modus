# ADR-IMPL-06 — Compatibility and deprecation policy

- **Status:** Accepted at G0 (2026-11-02); G5 reviews release compatibility
- **Date:** 2026-11-02
- **Owner:** tech lead + product owner
- **Reversal cost:** Medium

## Context

WIP is pre-1.0 and has breaking-change latitude, but plugin and capability contracts need a predictable compatibility story for 1.0 consumers.

## Decision

- Pre-1.0: breaking changes are allowed with a written migration note and, where practical, a one-release obsolete adapter.
- 1.0: public contracts follow semantic versioning; additive changes in minor versions, breaking changes in major versions only.
- Capability/workflow manifests carry an API/compatibility version; runtime negotiation fails deterministically for unsupported ranges.
- Deprecations are announced for at least one minor release; removal only in a major release.
- The `IWorkflow` adapter is removed in the release after 1.0.

## Consequences

- Positive: consumers can plan upgrades; plugin failures are deterministic.
- Negative: adapter/maintenance overhead; slower removal of legacy contracts.

## Reversal triggers

- A security fix cannot be expressed compatibly (security wins; documented exception).
- The product owner accepts a shorter deprecation window for a specific contract.

## Open questions

- Minimum supported plugin contract version at 1.0. Owner: tech lead.
- Deprecation window length (one minor vs one calendar quarter). Owner: product owner.
