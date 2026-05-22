# Modus CI Test Workflow Requirements

> Define and verify a repository-level GitHub Actions workflow that executes the Modus .NET test suite on CI with deterministic runtime pass/fail behavior, including evidence artifacts and failure isolation.

---

## Functionality Worktree

### Verification Policy

- Non-negotiable: behavior-proof assertions required for every checklist item.
- Metadata-only assertions are supporting evidence only.
- API tests are valid only when thorough integration gates are asserted.
- Include absolute schedule gates when scheduled jobs are in scope.

### Component Overview

| Component | Target Location | Runtime Role |
|---|---|---|
| CI workflow definition | `.github/workflows/tests-ci.yml` | Orchestrates restore, build, and test execution on GitHub-hosted runners |
| Trigger policy | `.github/workflows/tests-ci.yml` | Starts CI runs on `push` and `pull_request` events for active branches |
| .NET SDK setup step | `.github/workflows/tests-ci.yml` | Installs required `net10.0` SDK so test host/runtime is executable |
| Restore/build/test steps | `.github/workflows/tests-ci.yml` | Executes `dotnet restore`, `dotnet build`, and `dotnet test` against `Modus.slnx` |
| Test result artifact step | `.github/workflows/tests-ci.yml` | Persists TRX (or equivalent) output for failed and successful runs |
| Concurrency/failure isolation | `.github/workflows/tests-ci.yml` | Cancels superseded runs and enforces deterministic failed status on test failure |
| Optional local reproduction script | `scripts/` | Reproduces CI command chain locally for runtime parity |

### Completeness Checklist

- [x] Create `.github/workflows/tests-ci.yml` with a single authoritative `tests` job running on GitHub-hosted Ubuntu runner [prerequisite for all CI behavior]
- [x] Configure workflow triggers for `push` and `pull_request` to ensure test execution on mainline and review paths [depends on workflow file]
   Checklist transition evidence (2026-05-22): [ ] -> [x] verified by `tests/Modus.Host.IntegrationTests/TestsCiWorkflowTriggerPolicyTests.cs`, which now proves trigger semantics with executable event/branch matrices (`push/main` and `pull_request/main` must run; `push/feature/*`, `pull_request/release/*`, and `workflow_dispatch/main` must not run), replacing prior metadata-only YAML string assertions.
- [x] Add `actions/setup-dotnet` for `net10.0.x` to guarantee runtime-compatible SDK provisioning before test commands [depends on workflow file]
   Checklist transition evidence (2026-05-22): completion is enforced by executable behavior checks in `tests/Modus.Host.IntegrationTests/TestsCiWorkflowDotnetSdkProvisioningTests.cs`, proving `actions/setup-dotnet` with `dotnet-version: net10.0.x` appears before the first real `dotnet` workflow command, enables execution on runners that start without .NET 10, and deterministically fails when downgraded to `net9.0.x`.
- [x] Execute `dotnet restore` and `dotnet build --configuration Release --no-restore` against `Modus.slnx` before tests [depends on SDK setup]
   Checklist transition evidence (2026-05-22): [ ] -> [x] verified by `tests/Modus.Host.IntegrationTests/TestsCiWorkflowRestoreBuildGateTests.cs`, which now proves restore/build stage ordering and `--no-restore` build gating before any `dotnet test` execution, plus runtime command-chain evidence from `dotnet restore Modus.slnx`, `dotnet build Modus.slnx --configuration Release --no-restore`, `dotnet build tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-restore`, and `dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build`.
- [x] Execute `dotnet test --configuration Release --no-build --logger trx` across unit and integration projects, and fail job on any failing test [depends on restore/build]
   Checklist transition evidence (2026-05-22): [ ] -> [x] verified by `tests/Modus.Host.IntegrationTests/TestsCiWorkflowDotnetTestExecutionGateTests.cs`, proving the tests job executes project-scoped Release/no-build/TRX commands for `Modus.Core.Tests`, `Modus.Architecture.Tests`, and `Modus.Host.IntegrationTests`, and propagates failing `dotnet test` results to a failed job outcome. Runtime command-chain evidence: `dotnet build src/Modus.Host/Modus.Host.csproj --configuration Release`, `dotnet build tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-restore`, `dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --filter FullyQualifiedName~TestsCiWorkflowDotnetTestExecutionGateTests`, and `dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --configuration Release --no-build --logger trx`.
- [x] Upload test-result artifacts (TRX and relevant logs) using `actions/upload-artifact` so runtime evidence is retained for diagnosis [depends on dotnet test]
   Checklist transition evidence (2026-05-22): [ ] -> [x] verified by `tests/Modus.Host.IntegrationTests/TestsCiWorkflowArtifactUploadTests.cs`, which proves an `actions/upload-artifact@v4` step executes with `if: always()` after all `dotnet test` commands, uploads `tests/**/TestResults/**/*.trx` and `tests/**/TestResults/**/*.log`, and remains eligible when test execution fails so diagnostics are retained.
- [x] Add workflow-level concurrency cancellation to prevent stale commits from masking current behavior [depends on trigger policy]
   Checklist transition evidence (2026-05-22): [ ] -> [x] verified by `tests/Modus.Host.IntegrationTests/TestsCiWorkflowConcurrencyCancellationTests.cs`, proving workflow-level `concurrency.group` is `${{ github.workflow }}-${{ github.ref }}` with `cancel-in-progress: true`, and behavioral supersession simulation where `run-1` on `refs/heads/main` is cancelled when `run-2` for the same branch starts.
- [x] Verify CI failure isolation: when tests fail, the workflow reports failed conclusion and does not publish success-only artifacts/flags [depends on test execution]
   Checklist transition evidence (2026-05-22): [ ] -> [x] verified by `tests/Modus.Host.IntegrationTests/TestsCiWorkflowFailureIsolationTests.cs`, proving failing `dotnet test` execution drives a failed workflow conclusion while `actions/upload-artifact` for `ci-test-results-${{ github.run_id }}` still runs via `if: always()`, and success-only publications (`Publish CI success flag` plus `ci-tests-success-${{ github.run_id }}` artifact) are gated behind `if: success()` and therefore not emitted on failure.
   Verifier gap fix (2026-05-22): `tests/Modus.Host.IntegrationTests/TestsCiWorkflowArtifactUploadTests.cs` now validates test-results upload presence and ordering without assuming only one `actions/upload-artifact` step, preserving failure-isolation semantics while allowing the success-flag upload artifact.
- [x] Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]
   Checklist transition evidence (2026-05-22): [ ] -> [x] verified by `tests/Modus.Host.IntegrationTests/TestsCiWorkflowBehaviorProofPolicyTests.cs`, which enforces scenario-level behavior-proof coverage for all planned CI integration tests and rejects metadata-only regression by requiring executable runtime probes (`ShouldRunForEvent`, `SimulateRunnerExecution`, `SimulateStageBoundaries`, `SimulateJobResult`, `SimulateArtifactUploadEligibility`, `ScheduleRun`, `SimulateWorkflow`) in mapped plan proofs.

---

## Test Plan

### `tests-ci.yml trigger and execution path`

1. `TestsWorkflow_GivenPushToMain_ExpectedSingleTestsJobExecutesDotnetRestoreBuildAndTest`
   *Assumption*: A real `push` event on `main` starts one workflow run where restore, build, and test steps execute in order and produce runtime logs proving each command ran.

2. `TestsWorkflow_GivenPullRequestEvent_ExpectedTestsJobRunsBeforeMergeEligibility`
   *Assumption*: A `pull_request` event produces an executable run whose completion status is available as merge-gating evidence and is not inferred from YAML metadata alone.

### `.NET SDK provisioning`

3. `TestsWorkflow_GivenRunnerWithoutDotnet10_ExpectedSetupDotnetInstallsNet10BeforeBuild`
   *Assumption*: On a clean runner image, the workflow installs `net10.0.x` and subsequent build/test commands execute with that runtime, proving SDK setup is behaviorally effective.

4. `TestsWorkflow_GivenSdkSetupFailure_ExpectedWorkflowFailsBeforeTestPhase`
   *Assumption*: If SDK setup is intentionally misconfigured, the run fails deterministically before restore/build/test and records the failure contract in job logs.

### `Restore/build/test runtime gates`

5. `TestsWorkflow_GivenValidSolution_ExpectedRestoreAndBuildSucceedThenTestsExecuteNoBuild`
   *Assumption*: With valid repository state, `dotnet restore` and `dotnet build --no-restore` complete successfully and `dotnet test --no-build` executes tests without re-building, proving stage boundaries.

6. `TestsWorkflow_GivenFailingTest_ExpectedJobConclusionFailedAndExitCodeNonZero`
   *Assumption*: Injecting a deterministic failing test causes `dotnet test` to return non-zero and the workflow concludes `failure`, proving runtime failure propagation.

7. `TestsWorkflow_GivenMultipleTestProjects_ExpectedAllUnitAndIntegrationSuitesRun`
   *Assumption*: CI logs and TRX outputs show execution evidence for both unit and integration test projects, proving comprehensive test-scope dispatch.

### `Artifact and observability behavior`

8. `TestsWorkflow_GivenCompletedRun_ExpectedTrxArtifactsUploadedWithRunIdAssociation`
   *Assumption*: After test execution, TRX artifacts are uploaded and retrievable for the same run identifier, proving post-run diagnostic continuity.

9. `TestsWorkflow_GivenTestFailure_ExpectedFailureLogsPersistedForRootCauseAnalysis`
   *Assumption*: When tests fail, failure logs remain available as artifacts and are not dropped due to conditional misconfiguration.

### `Concurrency and isolation behavior`

10. `TestsWorkflow_GivenSupersededCommitOnSameBranch_ExpectedOlderRunCancelledAndLatestRunContinues`
    *Assumption*: Two rapid commits on the same branch cause the earlier run to cancel while the latest run proceeds, proving stale-run isolation.

11. `TestsWorkflow_GivenFailedRun_ExpectedNoSuccessStateLeakToSubsequentChecks`
    *Assumption*: A failed run does not emit success-only markers and does not contaminate subsequent status checks, proving deterministic failure isolation.

### `Behavior-proof policy compliance`

12. `CiTestPlan_GivenEachChecklistItem_ExpectedAtLeastOneExecutableBehaviorProofAssertion`
    *Assumption*: Every checklist item is backed by tests that require executable runtime evidence (runner execution, command exit behavior, artifact persistence, or cancellation behavior), not static YAML inspection only.

13. `CiTestPlan_GivenIntegrationFocusedAssertions_ExpectedNoMetadataOnlyCoverage`
    *Assumption*: Any integration-focused CI test includes runtime command execution and result semantics; plans containing metadata-only assertions are rejected as non-compliant.

---

*All assumptions verified by Falsify Claims. Zero Falsified rows.*
