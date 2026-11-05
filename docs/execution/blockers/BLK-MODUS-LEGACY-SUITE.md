# BLK-MODUS-LEGACY-SUITE — stale legacy integration meta-tests

**Status:** resolved 2026-11-05 — the suite is blocking again in Modus CI; see the resolution section.
**Date:** 2026-11-04
**Evidence:** clean Linux clone (`git` WIP @ d5cc3ef), full `Modus.Host.IntegrationTests` suite: 347 passed / 15 failed of 362; 6 of the 15 are WSL-only (no `make`, `zip`, `pwsh`) and pass on GitHub's ubuntu image, leaving **9 CI-relevant failures**, all stale meta-tests written against the pre-G0 unified workflow layout.

## Fixed in this run (no longer blocking)

- Migration regression gate (`MigrationRegression=true`): 19/19 on Windows and Linux. Causes: Windows `..\` include paths in five plugin projects; three `plugins/*.csproj` wrappers missing `ModusRuntimePluginType`; three wrappers missing from `Modus.slnx` (so CI never built their DLLs).
- Plugin endpoint curl probe: 2/2 on Linux once the wrapper assemblies are built.
- Modus.Host.IntegrationTests moved to Test SDK 17.14.1 (same parent-query switch as WIP); the migration step emits TRX-derived annotations.

## Remaining (CI-relevant, stale expectations)

| File | Tests | Why stale |
|---|---|---|
| `TestsCiWorkflowLinuxFirstWipGateTests` | 3 | Expects a `wip_linux_gate` job that was replaced by the `wip-build (ubuntu)` + `wip-offline-tests` jobs at the Tests CI / Modus CI split. |
| `TestsCiWorkflowConcurrencyCancellationTests` | 2 | Assert `concurrency.cancel-in-progress` on the job workflow; Modus CI has no concurrency block (Tests CI does). |
| `TestsCiWorkflowDotnetTestExecutionGateTests` | 2 | Assert the exact command set/order of the pre-split `tests` job. |
| `TestsCiWorkflowTriggerPolicyTests` | 1 | Asserts mainline-only triggers; the WIP branch is intentionally triggered too. |
| `TestsCiWorkflowFailureIsolationTests` | 1 | Simulates a failing `dotnet test` and expects the job to fail; the legacy suite is now explicitly non-blocking so the hard gates can stay green. |

## WSL-only (not CI)

`MakefileTargetsTests` (3) and the two upload/packaging script classes (3) fail in a bare WSL image without `make`, `zip` and `pwsh`; GitHub's ubuntu-latest image provides them.

## Interim policy

The two release-relevant gates (migration regression, plugin curl probe) are **hard** and green. The full legacy suite runs with `continue-on-error` and reports failures as `::warning` annotations; the `ci-tests-success` flag is only published when the legacy suite is also green. The step becomes blocking again once these 9 meta-tests are reconciled with the current workflow split in Phase 4.

## Resolution (2026-11-05)

- `TestsCiWorkflowLinuxFirstWipGateTests` and `TestsCiWorkflowDotnetTestExecutionGateTests` were rewritten against the current Tests CI split (platform builds, `tools/run-wip-tests.ps1`, the verifier gate, both architecture projects, job-outcome simulation for the behavior-proof policy).
- `TestsCiWorkflowTriggerPolicyTests` now expects `main` and `WIP`; `TestsCiWorkflowFailureIsolationTests` passes with the blocking step and `success()` success-flag conditions.
- Modus CI gained workflow-level concurrency and the legacy suite step is no longer `continue-on-error`; its failure report emits `::error`.
- The root `Makefile` regained the solution-level `build` / `test` / `test-no-build` / `run-host` targets the meta-tests require.
- The timer autonomous-loop test's initial wait was widened to a bounded 10 s (loaded runners starved it).
- Local Linux: 355/362 pass; the remaining 7 are WSL-only gaps (`pwsh`, `make`) that GitHub's ubuntu image provides.
- **Confirmed green:** Modus CI run `42056809397` on `6453c38` succeeded with the full legacy suite as a blocking step (the earlier `4e9c11d` run failed only in the failure-reporting step on a green TRX; fixed in `6453c38`).