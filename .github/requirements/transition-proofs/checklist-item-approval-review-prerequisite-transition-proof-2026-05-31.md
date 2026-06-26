# Checklist Transition Proof

Checklist item:
- Enforce approval prerequisite that current review evidence exists for the current diff hash at approval time, with deterministic stale-review rejection guidance [depends on RV-002 and RV-003 absolute review gate]

## Baseline Unchecked Source Text Evidence

Baseline witness file:
- .github/requirements/transition-proofs/baselines/checklist-item-approval-review-prerequisite.unchecked.snapshot-2026-05-31.md

Baseline capture:

```text
timestamp=2026-05-31T06:50:07-03:00
line=75:- [ ] Enforce approval prerequisite that current review evidence exists for the current diff hash at approval time, with deterministic stale-review rejection guidance [depends on RV-002 and RV-003 absolute review gate]
unchecked=6
checked=18
```

## Checked Completion Evidence

Checked capture:

```text
timestamp=2026-05-31T06:50:20-03:00
line=75:- [x] Enforce approval prerequisite that current review evidence exists for the current diff hash at approval time, with deterministic stale-review rejection guidance [depends on RV-002 and RV-003 absolute review gate] [transition-proof: .github/requirements/transition-proofs/checklist-item-approval-review-prerequisite-transition-proof-2026-05-31.md] [baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-approval-review-prerequisite.unchecked.snapshot-2026-05-31.md]
unchecked=5
checked=19
```

## Item-Specific Runtime Proof Tests

Updated and executed tests tagged with this checklist item:
- tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - ApprovalGate_GivenMissingReviewEvidence_RejectsApprovalWithDeterministicRecoveryGuidance
  - ApprovalGate_GivenMissingOrStaleReviewEvidence_RejectsApprovalWithDeterministicRecoveryGuidance

## Command Evidence

Build command:

```text
dotnet build .\src\Wip.Runtime\Wip.Runtime.csproj -c Debug --nologo
Build succeeded in 0.9s
```

Focused item-test command:

```text
dotnet test .\tests\Wip.Shell.E2E.Tests\Wip.Shell.E2E.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~ApprovalGate_GivenMissingReviewEvidence_RejectsApprovalWithDeterministicRecoveryGuidance|FullyQualifiedName~ApprovalGate_GivenMissingOrStaleReviewEvidence_RejectsApprovalWithDeterministicRecoveryGuidance"
Test summary: total: 2, failed: 0, succeeded: 2, skipped: 0
Build succeeded in 9.2s
```

Full target test project command:

```text
dotnet test .\tests\Wip.Shell.E2E.Tests\Wip.Shell.E2E.Tests.csproj -c Debug --no-build --nologo
Test summary: total: 59, failed: 0, succeeded: 59, skipped: 0, duration: 80.6s
Build succeeded in 80.8s
```

## Implementation and Behavioral Delta

Runtime enforcement update:
- src/Wip.Shell/Interactive/WipShellCommandLoop.cs
  - Added explicit pre-confirmation rejection for stale review summaries.
  - Added explicit pre-confirmation rejection when review summary diff hash does not match active worktree diff hash.
  - Added deterministic recovery guidance constant shared by approval prerequisite failures.

Behavior-proof test update:
- tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - Added missing-review-evidence approval rejection test.
  - Updated stale-review approval rejection assertion to match deterministic current-diff mismatch message.
