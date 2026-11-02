# WP-0.2 — Governance guard evidence

**Status:** done
**Date:** 2026-11-02
**Baseline:** tag `wip-baseline-2026-11-02` at `99c0b08`

## Acceptance evidence

| Criterion | Evidence |
|---|---|
| `run` stops at `Editing`/`Checkpointed` | Runtime guard tests: [runtime-guard.txt](runtime-guard.txt) — 3/3 passed |
| Shell `run` cannot reach `Validating`/`AwaitingApproval`/`Approved`/`Merged` | Shell E2E guard tests: [shell-guard.txt](shell-guard.txt) — 2/2 passed |
| `run` never sets `ValidationStatus=Passed` or `ApprovalStatus=Approved` | `ResolveApprovalStatusForTransition_GivenRunInducedStateAdvancement_ExpectedNeverSetsApprovedWithoutTokenArtifact` passed |
| A run followed by a merge attempt still requires validation/review/approval | `ShellProcess_GivenRunOnly_ExpectedMergeCommandStillRequiresApprovalValidationAndReviewArtifacts` passed |
| Test would fail if the guard were removed | The assertions compare persisted `State`, `ApprovalStatus`, and `ValidationStatus` after `run`; a regression to `Validating`/`Merged` or approved/passed fails them. No product mutation was performed. |
| Test runs in CI | Guard steps added to `.github/workflows/tests-ci.yml` for `Wip.Runtime.Tests` and `Wip.Shell.E2E.Tests` |

## Supporting fix

The runtime tests now use `TestRepositoryPaths` (workspace temp) instead of hardcoded `C:/repo`; this fixed 17 environment failures and made the runtime guard tests executable under the sandbox. The rerun is [test-runtime-after-path-fix.txt](../WP-0.1/test-runtime-after-path-fix.txt) (71/72; one cancellation flake).

## Residual

- DeepSeek cancellation test: **F-FLAKE**, 3/3 passes in isolation; tracked in [baseline-test-status.md](../../../plan/baseline-test-status.md) with owner/expiry.
- Shell E2E suite still has 2 unrelated failures (registry + recursive meta-test); tracked in the baseline failure register.
- G0 is not passed; WP-0.3–WP-0.6 remain.
