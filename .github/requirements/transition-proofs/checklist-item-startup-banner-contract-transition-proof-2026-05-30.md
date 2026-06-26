# Transition Proof

- Checklist item: Implement startup banner contract to include product/version, detected repository path, loaded plugin count, active policy profile, and help hint on shell launch [depends on SH-001 startup compliance]
- Date: 2026-05-30
- Scope: execution subagent repair round 1, non-git transition evidence closure

## Implementation Evidence

- Runtime startup banner emission is implemented in src/Wip.Shell/Interactive/WipShellCommandLoop.cs via WriteStartupBannerAsync.
- Process-level behavior-proof assertion exists in tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs via ShellProcess_GivenFreshLaunch_StartupBannerIncludesVersionRepoPluginCountPolicyAndHelpHint.
- Behavior-proof checklist mapping is explicit through [Trait("ChecklistItem", StartupBannerChecklistItem)] on that test.
- Immutable source anchors:
  - tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs:37 (SHA256: bf78e7387dc5691ff7a92bcc10f9f7ee1f560b30f3e72fb6ea72b7c98c010de1)
  - src/Wip.Shell/Interactive/WipShellCommandLoop.cs:177 (SHA256: 062b344ca9aa28be8b72d3d3142dbe1a66abbace303005c20d67d642a666fbbc)

## Baseline Unchecked Source Text Evidence

- Baseline witness artifact:
  - .github/requirements/transition-proofs/baselines/checklist-item-startup-banner-contract.unchecked.snapshot-2026-05-30.md
- Deterministic unchecked baseline line:
  - - [ ] Implement startup banner contract to include product/version, detected repository path, loaded plugin count, active policy profile, and help hint on shell launch [depends on SH-001 startup compliance]
- Unchecked baseline line SHA256:
  - a4c429c6ace22dba8a16e8a9e47463e9a5ef3ca76f89bca7c7fe2251a3ed775f

## Checked Completion Evidence

- Requirements locator: .github/requirements/Wip.Next-Steps.md:54
- Checked checklist line:
  - - [x] Implement startup banner contract to include product/version, detected repository path, loaded plugin count, active policy profile, and help hint on shell launch [depends on SH-001 startup compliance] [transition-proof: .github/requirements/transition-proofs/checklist-item-startup-banner-contract-transition-proof-2026-05-30.md] [baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-startup-banner-contract.unchecked.snapshot-2026-05-30.md]
- Checked line SHA256:
  - 41f9bf94e1899414d5f790289bc6b6705fe653b1d4da297205c9be00885fee62

## Command Evidence

1. dotnet build Modus.slnx -c Debug --nologo
- Exit code: 0
- Summary: Build succeeded, 0 Warning(s), 0 Error(s), Time Elapsed 00:00:02.61.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-startup-banner-contract-build-2026-05-30.txt

2. dotnet test tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj -c Debug --no-build --nologo --filter "FullyQualifiedName~ShellProcess_GivenFreshLaunch_StartupBannerIncludesVersionRepoPluginCountPolicyAndHelpHint|FullyQualifiedName~BehaviorProofCompliance"
- Exit code: 0
- Summary: Failed=0, Passed=6, Skipped=0, Total=6, Duration=50 s.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-startup-banner-contract-test-2026-05-30.txt

3. dotnet test tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj -c Debug --no-build --nologo --filter "FullyQualifiedName~BehaviorProofCompliance|FullyQualifiedName~ShellProcess_GivenInitSessionStartPlanDiffValidateReviewApproveMerge_ExpectedAcceptanceTranscriptSucceeds"
- Exit code: 0
- Summary: Failed=0, Passed=6, Skipped=0, Total=6, Duration=50 s.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-startup-banner-contract-test-gate-2026-05-30.txt

## Completion Decision

- Checklist item status is [x] with linked transition-proof and linked baseline witness.
- Non-git independent transition semantics are satisfied by deterministic unchecked and checked line evidence captured in tracked artifacts.
