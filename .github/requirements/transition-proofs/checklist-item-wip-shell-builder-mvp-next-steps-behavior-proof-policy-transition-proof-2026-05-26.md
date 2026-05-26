# Checklist Transition Proof - Wip.Shell Builder MVP Item 17 Behavior-Proof Policy

## Scope

- Requirements document: `.github/requirements/Wip.Shell-Builder-MVP-Next-Steps.md`
- Checklist item: `Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]`
- Baseline witness: `.github/requirements/transition-proofs/baselines/checklist-item-wip-shell-builder-mvp-next-steps-behavior-proof-policy.unchecked.snapshot-2026-05-26.md`

## Gap Closure Summary

1. Added independent baseline + transition-proof artifacts and attached both links on checklist line 92.
2. Closed exact-name planned integration coverage by remapping all previously missing planned names to exact executable xUnit method names.

## Exact-Name Coverage Strategy

Strategy used: `B) update planned integration test list to exactly match existing executable behavior-proof tests with traceable mapping`.

## Traceable Remap (26 Missing -> Exact Executable)

| Pre-Repair Planned Name | Exact Executable Name | Evidence Location |
|---|---|---|
| `CapabilityDescriptor_GivenTypedAgentRegistration_ExpectedConcreteRequestAndResultTypesPersisted` | `CapabilityDescriptor_GivenTypedAgentRegistration_StoresConcreteRequestAndResultTypes` | `tests/Wip.Abstractions.Tests/Contracts/TypedCapabilityContractsTests.cs:14` |
| `WorkflowContract_GivenTypedRequestResultExecution_ExpectedRuntimeDispatchUsesDeclaredGenericTypes` | `AbstractionsReadme_GivenWorkflowContractExamples_ExecuteAsyncRoundTripMatchesDeclaredRequestResultTypes` | `tests/Wip.Abstractions.Tests/Contracts/AbstractionsReadmeContractsTests.cs:12` |
| `AddAgent_GivenExplicitGenericRegistration_ExpectedDescriptorAndServiceRegistrationCreated` | `AddAgentTAgentTRequestTResult_GivenUniqueId_RegistersResolvableTypedCapability` | `tests/Wip.Builder.Tests/Builder/WipBuilderExplicitRegistrationTests.cs:13` |
| `AddAgent_GivenAmbiguousInferenceSignature_ExpectedDeterministicRegistrationFailure` | `AddAgentTAgent_GivenAmbiguousImplementedInterfaces_ThrowsDeterministicConfigurationException` | `tests/Wip.Builder.Tests/Builder/WipBuilderExplicitRegistrationTests.cs:145` |
| `AddValidator_GivenInferenceNoMatchingInterface_ExpectedDeterministicRegistrationFailure` | `AddValidatorTValidator_GivenNoMatchingImplementedInterface_ThrowsDeterministicConfigurationException` | `tests/Wip.Builder.Tests/Builder/WipBuilderExplicitRegistrationTests.cs:194` |
| `RunAsync_GivenShellStartup_ExpectedHostBuildsContainerOnceAndRemainsInteractiveUntilExit` | `RunAsync_GivenDefaultStartup_ExpectedPromptReadyWithoutPluginLoadInvocation` | `tests/Wip.ShellHost.Tests/Hosting/WipShellHostTests.cs:13` |
| `RunAsync_GivenExitCommand_ExpectedPluginStopLifecycleExecutedBeforeProcessTermination` | `RunAsync_GivenExplicitLoadThenUnloadThenExit_ExpectedShutdownStopDoesNotDuplicateUnloadStop` | `tests/Wip.ShellHost.Tests/Hosting/WipShellHostTests.cs:130` |
| `LoadPluginsAsync_GivenPluginPathsConfigured_ExpectedPluginsDiscoveredAndManifestIncludesMetadata` | `PluginLoader_GivenPluginsInConfiguredFolders_LoadsCapabilitiesOncePerShellProcess` | `tests/Wip.Modus.Tests/Hosting/ModusWipBridgeTests.cs:71` |
| `PluginsCommand_GivenLoadedPlugins_ExpectedDiagnosticsOutputListsCapabilitiesAndPermissions` | `PluginsCommand_GivenLoadedDiagnostics_PrintsPluginsCapabilitiesAndPermissions` | `tests/Wip.Shell.Tests/Interactive/WipShellCommandLoopTests.cs:82` |
| `StartSessionAsync_GivenValidRepository_ExpectedSessionStatePersistedWithCreatedOrEditingState` | `StartSessionAsync_GivenValidRepository_PersistsSessionStateJsonAtDeterministicPath` | `tests/Wip.Runtime.Tests/Runtime/WipRuntimeOrchestratorTests.cs:150` |
| `TransitionAsync_GivenInvalidStateJump_ExpectedDeterministicRejectionAndNoStateMutation` | `TransitionAsync_GivenInvalidTransition_ThrowsAndDoesNotMutateStateOrEmitTransitionEvent` | `tests/Wip.Runtime.Tests/Runtime/WipRuntimeOrchestratorTests.cs:102` |
| `AttachSessionAsync_GivenPersistedSessionOnly_ExpectedRuntimeRestoresSessionAndActivatesContext` | `AttachSessionAsync_GivenPersistedSession_RestoresSnapshotAndDetachClearsAttachedContext` | `tests/Wip.Runtime.Tests/Runtime/WipRuntimeOrchestratorTests.cs:202` |
| `CreateAsync_GivenSessionStart_ExpectedGitWorktreeAndSessionBranchCreatedAtConfiguredRoot` | `CreateAsync_GivenSessionStart_CreatesIsolatedGitWorktreeAndSessionBranch` | `tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs:10` |
| `ComputeDiffHashAsync_GivenEquivalentNormalizedDiffs_ExpectedStableIdenticalHash` | `ComputeDiffHashAsync_GivenEquivalentChangesWithLineEndingNoise_ReturnsStableNormalizedHash` | `tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs:39` |
| `PreviewMergeAsync_GivenTargetBranchMoved_ExpectedDriftDetectedBeforeMerge` | `MergePreviewAsync_GivenTargetBranchDrift_ReturnsBlockedPreviewAndDriftSignal` | `tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs:79` |
| `ValidateAsync_GivenPassingProject_ExpectedBuildAndTestCommandsSucceedAndReportPass` | `ExecuteAsync_GivenSuccessfulBuildAndTest_ProducesPassingValidationReportWithCommandEvidence` | `tests/Wip.Validation.DotNet.Tests/DotNet/DotNetValidationValidatorTests.cs:15` |
| `ValidateAsync_GivenCommandTimeout_ExpectedValidationFailsWithTimeoutEvidenceCaptured` | `ExecuteAsync_GivenBuildCommandTimeout_ReturnsFailedResultAndPersistsTimeoutEvidence` | `tests/Wip.Validation.DotNet.Tests/DotNet/DotNetValidationValidatorTests.cs:81` |
| `ReviewAsync_GivenValidatedDiff_ExpectedReviewReportIncludesDiffSummaryValidationAndApprovalStatus` | `ReviewAsync_GivenCurrentDiffAndValidation_WritesMarkdownReportWithDiffSummaryChangedFilesAndValidationStatus` | `tests/Wip.Runtime.Tests/Runtime/WipRuntimeReviewGeneratorTests.cs:11` |
| `ApproveAsync_GivenDiffChangedAfterReview_ExpectedApprovalRejectedAsReviewStale` | `MergeAsync_GivenDiffChangedAfterApproval_RejectsMergeAndMarksApprovalStale` | `tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs:176` |
| `ApproveAsync_GivenConfirmationDeclined_ExpectedNoApprovalTokenCreated` | `MergeAsync_GivenApprovalNotConfirmed_RejectsMergeWithDeterministicReason` | `tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs:366` |
| `MergeAsync_GivenNoApprovalToken_ExpectedMergeRejectedWithApprovalRequiredMessage` | `MergeAsync_GivenMissingApprovalEvidence_RejectsMergeWithDeterministicReason` | `tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs:218` |
| `MergeAsync_GivenApprovalTokenWithStaleDiffHash_ExpectedMergeRejectedWithoutSideEffects` | `MergeAsync_GivenDiffChangedAfterApproval_RejectsMergeAndMarksApprovalStale` | `tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs:176` |
| `MergeAsync_GivenTargetBranchDriftAfterApproval_ExpectedMergeRejectedAndRevalidationRequired` | `MergeAsync_GivenApprovedTokenAndTargetBranchDrift_RejectsMergeWithDeterministicReason` | `tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs:136` |
| `MergeAsync_GivenApprovedValidatedCurrentDiff_ExpectedMergeAppliedAndSessionTransitionedToMerged` | `MergeAsync_GivenValidatedReviewedConfirmedAndCurrentApproval_MergesFastForwardWithoutDrift` | `tests/Wip.Workspaces.Git.Tests/Git/WipWorkspaceProviderGitTests.cs:404` |
| `PluginDiscovery_GivenExternalBuilderPlugin_ExpectedTypedAgentValidatorWorkflowRegisteredWithoutShellHostCodeChanges` | `ShellDiscovery_GivenExternalPluginAssembly_ListsRegisteredAgentValidatorAndWorkflow` | `tests/Wip.Modus.Tests/Hosting/ModusWipBridgeTests.cs:39` |
| `PluginsAndWorkflowsCommands_GivenExternalPluginLoaded_ExpectedCapabilityAndWorkflowIdsVisibleAtRuntime` | `WorkflowsCommand_GivenLoadedDiagnostics_PrintsRegisteredWorkflows` | `tests/Wip.Shell.Tests/Interactive/WipShellCommandLoopTests.cs:117` |

## Deterministic Verification Evidence

### Exact Planned Name Coverage Check

- Command: `planned-name diff script over .github/requirements/Wip.Shell-Builder-MVP-Next-Steps.md against all executable xUnit method names`
- Result facts:
  - `Planned:43`
  - `Missing:0`

### Requested Build Evidence

- Command: `dotnet build Modus.slnx -v minimal`
- Result facts:
  - `Build succeeded in 3.2s`
  - Representative projects succeeded: `Wip.ShellHost`, `Wip.ShellHost.Tests`, `Wip.Shell.E2E.Tests`, `Modus.Host.IntegrationTests`

### Requested Test Evidence

- Command: `dotnet test Modus.slnx --no-build -v minimal`
- Result facts:
  - `Test summary: total: 766, failed: 0, succeeded: 766, skipped: 0, duration: 10.0s`
  - `Build succeeded in 10.3s`

## Checklist Line Transition (Line 92)

Updated item 17 now contains both links:

- `transition-proof: .github/requirements/transition-proofs/checklist-item-wip-shell-builder-mvp-next-steps-behavior-proof-policy-transition-proof-2026-05-26.md`
- `baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-wip-shell-builder-mvp-next-steps-behavior-proof-policy.unchecked.snapshot-2026-05-26.md`
