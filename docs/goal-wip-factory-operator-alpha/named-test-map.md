# Named-test map — v0.3 §12.2–12.3 vs the repository

Measured with `dotnet test --list-tests` across the four suites: **134 executable test methods**,
**15 of 80** spec-named tests present under the exact specified name.

This document classifies the **65 absent names** rather than renaming work that already exists. The
distinction matters: a missing *name* over an existing behaviour is a documentation defect; a missing
*name* over a missing behaviour is a product defect. Several are the second kind.

## A. Behaviour exists, only the name differs (rename/mapping — documentation defect)

| Spec name | Existing test |
|---|---|
| `Intake_GivenFileWithoutAcceptanceExpectations_IsRefused` | `Intake_GivenUnsupportedClassOrMissingExpectations_IsRefusedBeforeImplementation` |
| `Intake_GivenUnsupportedClass_IsRefusedBeforeImplementation` | same test, other half |
| `Lifecycle_GivenContractProposal_RecordsWithoutStateTransition` | `Lifecycle_GivenContractProposal_RecordsWithoutStateTransition` ✅ (already exact) |
| `Contract_GivenWrongImplementation_FailsOriginalScenarios` | exact ✅ |
| `ToolGateway_GivenApplyPatchConflict_RefusesAtomically` | exact ✅ |
| `Delivery_GivenVerifiedCandidate_ReportsProcessStoppedNotDeployed` | `Supervisor_GivenServeExit_DoesNotClaimAlwaysOnDaemon` (adjacent; asserts reporting honesty but not the exact message) |
| `Production_GivenEmptyDiff_FailsNoAttributableChange` | `Production_GivenEmptyDiff_FailsNoAttributableChange` — same assertion, different casing/wording in file |

Estimated: **6 names** fall here.

## B. Behaviour absent — the name cannot be honestly written yet (product defect)

These are the important ones. Each is a requirement in v0.3 that the implementation does not meet,
which is why no test with that name exists:

| Spec name(s) | Missing mechanism |
|---|---|
| `Host_GivenConcurrentRun_ExitsHostLockHeld` | **No OS host lock.** v0.3 §5.3 requires an exclusive lock; none is implemented. |
| `Claim_GivenCrashAfterClaim_IsReclaimableAfterDeadline`, `Claim_GivenLiveClaim_IsNotDispatchedTwice` | **No reservation.** `ClaimUnpublishedAsync` reads `!Published` and returns; it never writes a claim owner/deadline. |
| `StageHandler_GivenExhaustedAttempts_QuarantinesWithPersistedCount` | **Attempt counts are in-memory**; only quarantine is durable. |
| `Operation_GivenThreeWritesInStage_HasThreeUniqueStableIds`, `Operation_GivenReplayOfSameEffect_ReusesOriginalOperationId`, `Operation_GivenMergeAlreadyApplied_ReconcilesWithoutSecondMerge`*, `Operation_GivenUncertainMergeState_ReconcilesGitBeforeRetry`, `Operation_GivenDeliveryAlreadyRunning_ReconcilesOwnership` | **No operation-ID/ledger subsystem.** Reconciliation exists inside `GitMergeEngine` but is not represented as durable operation records. |
| `Intake_GivenTamperedFrozenContract_BlocksWorkItem` | **No tamper check** on a frozen contract. |
| `Contract_GivenAmbiguousUnobservableProse_RequiresResolutionBeforeFreeze`, `Contract_GivenModelAttemptToEditFrozenOracle_IsRefused`, `Contract_GivenProposalChanged_InvalidatesPriorAuthorization` | **No agent-write prohibition** on the contract and no invalidate-on-change path. |
| `Contract_GivenApprovedTemplateAndValidParameters_AutoApprovesWithProvenance` and the other five template tests | **D-09 template policy is not implemented.** Only human approval exists. |
| `Lifecycle_GivenSubmission_EmitsConsumableRequirementSubmitted`, `Lifecycle_GivenPendingContractApproval_StopsWithoutBusyLoop`, `Lifecycle_GivenEarlyFailure_UsesLegalBlockedOrCancelledTransition`, `Lifecycle_GivenPreMergeDeliveryFailure_TransitionsToNeedsRework`, `Lifecycle_GivenReadyForDelivery_RequiresDistinctMergeAuthorization`, `Lifecycle_GivenPostMergeContractFailure_NeverRecordsReleased` | Partly implemented; the rework and approval paths exist, but pre-merge `DeliveryValidation → NeedsRework` (D-08 A) is **not** wired: the handler records a failure event instead of taking the edge. |
| `Store_GivenSubmitApproveServeConcurrentProcesses_SerializesMutations`, `Store_GivenDependencyRelease_BlockedItemWakesOnce`, `Store_GivenRestart_OperatorStatusReadsDurableState` | No cross-process store lock; `Blocked` wake-up not implemented; status-from-durable **is** implemented. |
| `Supervisor_GivenSecondServe_RefusesDuplicateOwnership`, `Supervisor_GivenRestart_UsesPinnedReleaseArtifact` | No supervisor lease; serve materialises and stops, it does not survive restart. |
| `Verification_GivenHeadChangedAfterReview_RefusesMergeApproval`, `Verification_GivenMergeCommitDifferentFromHead_RunsContractOnMergedTree` | Second is implemented (post-merge verification runs on the merged tree); first is covered by PA-06 tests under different names. |
| `AgentLoop_GivenStepBudgetExhausted_FailsWithCategory`, `AgentLoop_GivenCancellation_StopsBeforeNextModelCall` | Budget exists in the dispatcher; cancellation-before-next-call is untested. |
| `Claim_*`, `PA02_GivenDifferentBlindRequirement_UsesUnchangedFactoryBinary` | PA-02 needs the blind pair (OQ-001). |
| `Workflow_GivenRealFactoryHost_NeverInvokesDemoImplementation` | Not proven: the demo verbs still exist and nothing asserts the operator path never reaches them. |

Estimated: **~59 names** fall here.

## C. Conclusion

The named-test gap (65) is **not mostly a naming problem**. Approximately **6** are renames; the rest
correspond to mechanisms v0.3 requires and the alpha does not have — most consequentially:

1. **No host lock** (single-writer safety is claimed, not enforced).
2. **No claim reservation** (exactly-once is reconciled in one path, not in the claim protocol).
3. **No operation-ID ledger** (reconciliation is ad hoc inside the merge engine).
4. **No D-09 template policy** (only human approval).
5. **D-08 A edge not wired** (pre-merge delivery failure records an event instead of `NeedsRework`).

Naming tests to match the spec without building these would convert a product gap into a documentation
illusion. That is exactly the failure mode this harness exists to prevent, so the map records the gap
instead.
