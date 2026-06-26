# Transition Proof

- Checklist item: Add explicit target-branch-drift recovery contract defining required rebase/revalidate/refresh steps and observable session state transitions before retrying merge [depends on AP-005 target drift recovery guidance]
- Date: 2026-05-31
- Scope: one-item iterative implementation closure for AP-005 drift recovery guidance

## Functional Tests Added or Updated

- Updated test: tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - TargetBranchDriftRecovery_GivenRuntimeContract_ExposesRequiredStepsAndObservableRetryStates
  - Strengthened to assert exact ordered recovery commands and exact session-state transition mapping through merge retry readiness.
- Existing runtime behavior proof test executed for this item:
  - Merge_GivenTargetBranchDriftAfterApproval_RejectsWithRefreshGuidanceAndNoBranchMutation

## Implementation Members and Files

- Existing implementation validated for this checklist item:
  - src/Wip.Runtime/Runtime/TargetBranchDriftRecoveryContract.cs
    - TargetBranchDriftRecoveryStep
    - TargetBranchDriftRecoveryContract
    - TargetBranchDriftRecovery.Current
    - TargetBranchDriftRecovery.FormatGuidance()
  - src/Wip.Shell/Interactive/WipShellCommandLoop.cs
    - Merge failure path emits TargetBranchDriftRecovery.FormatGuidance() when target drift is detected.
- No additional production implementation members were required in this cycle.

## Baseline Unchecked Source Text Evidence

- Baseline witness artifact:
  - .github/requirements/transition-proofs/baselines/checklist-item-target-branch-drift-recovery-contract.unchecked.snapshot-2026-05-31.md
- Deterministic unchecked baseline line witness:
  - - [ ] Add explicit target-branch-drift recovery contract defining required rebase/revalidate/refresh steps and observable session state transitions before retrying merge [depends on AP-005 target drift recovery guidance]
- Unchecked baseline line SHA256:
  - cac5cae0afea1fcb8292f3f5d7888e99821fa09a97501d8d8bfd58e16a8df9f5

## Checked Completion Evidence

- Requirements locator: .github/requirements/Wip.Next-Steps.md:79
- Checked checklist line:
  - - [x] Add explicit target-branch-drift recovery contract defining required rebase/revalidate/refresh steps and observable session state transitions before retrying merge [depends on AP-005 target drift recovery guidance] [transition-proof: .github/requirements/transition-proofs/checklist-item-target-branch-drift-recovery-contract-transition-proof-2026-05-31.md] [baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-target-branch-drift-recovery-contract.unchecked.snapshot-2026-05-31.md]
- Checked line SHA256:
  - 34bb6bef38020903905e74ac65179430fd2cd01e75cc195c6991f1631fca3ca7
- Checklist counters after completion:
  - unchecked=1
  - checked=23

## Command Evidence

1. dotnet build .\\src\\Wip.Runtime\\Wip.Runtime.csproj -c Debug --nologo
- Exit code: 0
- Summary: Build succeeded, 0 Warning(s), 0 Error(s), Time Elapsed 00:00:00.97.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-target-branch-drift-recovery-contract-build-2026-05-31.txt

2. dotnet test .\\tests\\Wip.Shell.E2E.Tests\\Wip.Shell.E2E.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~Merge_GivenTargetBranchDriftAfterApproval_RejectsWithRefreshGuidanceAndNoBranchMutation|FullyQualifiedName~TargetBranchDriftRecovery_GivenRuntimeContract_ExposesRequiredStepsAndObservableRetryStates"
- Exit code: 0
- Summary: Failed=0, Passed=2, Skipped=0, Total=2, Duration=3s.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-target-branch-drift-recovery-contract-test-focused-2026-05-31.txt

3. dotnet test .\\tests\\Wip.Shell.E2E.Tests\\Wip.Shell.E2E.Tests.csproj -c Debug --no-build --nologo
- Exit code: 0
- Summary: Failed=0, Passed=64, Skipped=0, Total=64, Duration=1m 23s.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-target-branch-drift-recovery-contract-test-full-2026-05-31.txt

4. Item status and exact line transition evidence commands
- Exit code: 0
- Summary: line=79 captured with checked form, unchecked and checked counters captured, and unchecked/checked line SHA256 values persisted.
- Full log artifacts:
  - .github/requirements/transition-proofs/evidence/checklist-item-target-branch-drift-recovery-contract-status-2026-05-31.txt
  - .github/requirements/transition-proofs/evidence/checklist-item-target-branch-drift-recovery-contract-line-transition-2026-05-31.txt

## Completion Decision

- The exact checklist line is [x] and now includes deterministic links to transition-proof and baseline witness artifacts.
- Concrete unchecked-to-checked evidence for this exact text exists via deterministic line witnesses and SHA256 hashes in tracked artifacts.
- Per-item baseline and transition-proof artifacts now exist under .github/requirements/transition-proofs as required.

## Why This Proof Does Not Depend on Git History

This proof chain does not rely on commit history for .github/requirements/Wip.Next-Steps.md. The file is newly introduced in this branch, so no stable HEAD history exists for this path. The proof therefore uses deterministic source-text witnesses (exact unchecked and checked lines plus SHA256 hashes), linked baseline and transition artifacts, and reproducible build/test command logs under .github/requirements/transition-proofs/evidence.
