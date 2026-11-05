# G4 gate packet — runtime seams accepted

**Gate:** G4 "workflow/DI contracts accepted" (internal engineering gate; the human release gate is G5).
**Date:** 2026-11-05
**Branch / HEAD:** `WIP` @ the commit carrying this packet.

## Scope — Phase 2 work packages

| WP | Delivered | Evidence |
|---|---|---|
| WP-2.1 | Host-managed DI: the runtime never builds a provider; the host owns composition and the per-run async scope. | WP-2.1 notes; architecture rule `runtime-no-nested-provider`; CI. |
| WP-2.2 | `StrategyPool.Instance` removed; pools are DI-owned. | WP-2.2 notes. |
| WP-2.3 | Declarative workflow definitions (`IWorkflowDefinition`, `WorkflowPlan`, `StageMap`), plan-scoped initial requests, and `IWorkflowExecutor` with the default `LinearWorkflowExecutor`; a registered executor takes over the run. | WP-2.3 notes; `WipRuntimeOrchestratorTests` (declarative + custom executor). |
| WP-2.4 | The process-wide stage-map registry and reflection/property heuristics are deleted. Definitions use their own plans; legacy linear registrations get `LinearWorkflowPlanFactory` with explicit maps and a per-registration run-request factory. Every stage input flows through `WorkflowPlanMapRuntime`. | WP-2.4 notes; `LinearWorkflowPlanFactory`; gate green. |
| WP-2.5 | Bones is a declarative definition (four stages, three explicit maps, initial Observe request) and runs through the plan; the legacy Bones compilation/registration is gone. | WP-2.5 evidence in WP-2.4 notes; Bones suite. |
| WP-2.6 | `Wip.ShellHost` has no `Wip.Bones.*` reference; the `WIP_BONES_E2E` registration lives in the test-only `Wip.Bones.E2E.Host`; ADR-IMPL-07 superseded and its waivers deleted. | WP-2.6 notes; architecture suite (42 projects, zero waivers). |
| WP-2.7 | Collectible per-plugin `AssemblyLoadContext` with shared-contract unification, per-plugin dependency resolution, deterministic unload, and load/unload diagnostics. | WP-2.7 notes; conflict-version fixture; bridge/ALC tests. |

## Verification at the gate

| Check | Result |
|---|---|
| Offline gate (`tools/run-wip-tests.ps1` + verifier) | **16/16 projects, 1,112 executed / 1,089 passed / 23 failed (all waived), zero unwaived, verifier exit 0** |
| Architecture | 4/4; 42-project graph; `architecture-waivers.json` is empty |
| Sandbox worker | 18/18 |
| Bones | 396 total / 394 passed / 2 pre-existing waived |
| Modus bridge (plugin ALC) | 24 total / 23 passed / 1 pre-existing waived |
| Solution build (Release) | green |
| Security CI | green (Linux + Windows jobs) |
| Tests CI | latest run for this HEAD (recorded in `docs/execution/state.json`) |

## Known limits carried forward (not G4 blockers)

- Windows OS-level containment (C1) is still not claimed; AppContainer helper fails with error 5. G2 remains conditional/trusted-only until 2026-12-03.
- The legacy `Modus CI` full integration suite is non-blocking (`BLK-MODUS-LEGACY-SUITE`, Phase 4); its release-relevant migration/curl gates are hard and green.
- No release claim before G5.

## Decision requested

Accept G4: the workflow/DI contracts are explicit, host-owned and enforced by tests and architecture rules, so Phase 2 is complete and the work may proceed to the permission model (WP-1.7 / G3) and Phase 3.