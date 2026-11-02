# CI gate errors — run 41483737752

**Gate:** `test_gate=FAIL errors=11` (exit 1).
**Commit:** `243769c`; **job:** `wip-offline-tests` (17m18s).

GitHub exposed 9 of the 11 error annotations; the remaining 2 were truncated by the annotation cap. The full gate log is in the uploaded artifact.

## Visible failures

### Wip.Bones.Host.Tests (7 visible)

- `BonesHostViewerLearningLoopStatusTests.BonesHostViewerLearningLoopStatus_GivenLiveMatchPublish_ExpectedTurnOfYIncreasesWithFrameCount`
- `BonesLearningLoopPromotionHostTests.BonesLearningLoop_GivenStrongCandidateEnhancement_ExpectedCandidateBecomesActiveForNextIteration`
- `BonesCoLearningHostTests.BonesCoLearningHost_GivenRestartWithResumeFromBest_ExpectedAllFourSeatsLoadLibraryStrategies`
- `BonesHostDeepSeekIntegrationTests.BonesHostDeepSeekIntegration_GivenMockHandlerReturningDistinctStrategy_ExpectedPersistedStrategyDiffersFromStubBoilerplate`
- `StatusApiFixesBehaviorProofComplianceGateTests.BehaviorProofCompliance_GivenStatusApiFixesChecklistItems_RequiresExecutableRuntimeProofForEachItem`
- `BonesHostViewerLearningLoopStatusTests.BonesHostViewerLearningLoopStatus_GivenRunningLoop_ExpectedSidebarStageTracksStatusApi`
- `BonesHostDeepSeekIntegrationTests.BonesHostDeepSeekIntegration_GivenMockHandlerReturningPlayMoves_ExpectedMatchTranscriptReflectsMockedSelections`

### Wip.Bones.Tests (1 visible)

- `BonesBoardOrientationBehaviorProofComplianceGateTests.BehaviorProofComplianceGate_GivenBoardOrientationChecklist_ExpectedAllTraitsBound`

### Wip.Modus.Tests (1 visible)

- `WipContributorArchitectureReadmeTests.WipContributorArchitectureReadme_GivenArchitectureMap_ContributorsCanTraceSessionCommandToRuntimeComponents`

## Triage and fixes pushed in `3f0a4da`

- **Bones host/meta tests:** these launch Chromium/Playwright; the offline job installed no browser. Added `playwright.ps1 install chromium` before the suites.
- **Modus README test:** the README trace used paths with trailing punctuation and a `bin/Debug` artifact path. Wrapped paths in code spans and changed the artifact path to `bin/Release`; all extracted paths now exist locally.
- **Bones board meta-test:** executes mapped Web tests and depends on the same Chromium install; rerun will confirm.

## Rerun

WIP run **41486872592** on `3f0a4da` is in progress. WP-0.4 remains blocked until its gate is green; no waivers were added for these 11.
