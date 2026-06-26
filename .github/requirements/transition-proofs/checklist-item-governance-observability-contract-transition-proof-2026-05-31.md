# Transition Proof

- Checklist item: Add observability contract that each governance command emits correlation-linked diagnostics and artifact references sufficient to reconstruct failed session decisions offline [depends on NFR observability hardening]
- Date: 2026-05-31
- Scope: one-item iterative implementation closure for governance observability

## Functional Tests Added or Updated

- Existing executable behavior-proof test for this checklist item:
  - tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - GovernanceDiagnostics_GivenFailedSessionDecision_EmitsCorrelationLinkedLogsAndArtifactReferencesForOfflineReconstruction
- Trait mapping to exact checklist item text:
  - [Trait("ChecklistItem", GovernanceDiagnosticsChecklistItem)]

## Implementation Members and Files

- Existing runtime implementation members verified for this checklist item:
  - src/Wip.Runtime/Runtime/WipRuntimeOrchestrator.cs
  - CreateGovernanceCorrelationId(SessionId sessionId, string command)
  - BuildGovernanceDiagnostics(string command, string outcome, SessionSnapshot snapshot)
  - CollectMergeDecisionArtifactReferencesAsync(SessionId sessionId, IArtifactStore artifactStore, CancellationToken cancellationToken)
  - MergeAsync(...): emits MergeAttempted and MergeFailed with shared correlation ID, command diagnostics, failure reason diagnostics, and decision artifact references
- Existing event payload contract used by persisted lifecycle journal:
  - src/Wip.Runtime/Runtime/SessionEvents.cs
  - SessionEvent(..., string? CorrelationId, IReadOnlyList<SessionEventDiagnostic>? Diagnostics, IReadOnlyList<SessionEventArtifactReference>? ArtifactReferences)

## Baseline Unchecked Source Text Evidence

- Baseline witness artifact:
  - .github/requirements/transition-proofs/baselines/checklist-item-governance-observability-contract.unchecked.snapshot-2026-05-31.md
- Committed-history probe evidence:
  - .github/requirements/transition-proofs/evidence/checklist-item-governance-observability-head-wip-next-steps-2026-05-31.txt
- Unchecked source-text witness captured in baseline:
  - - [ ] Add observability contract that each governance command emits correlation-linked diagnostics and artifact references sufficient to reconstruct failed session decisions offline [depends on NFR observability hardening]

## Checked Completion Evidence

- Requirements locator: .github/requirements/Wip.Next-Steps.md:80
- Checked checklist line:
  - - [x] Add observability contract that each governance command emits correlation-linked diagnostics and artifact references sufficient to reconstruct failed session decisions offline [depends on NFR observability hardening] [transition-proof: .github/requirements/transition-proofs/checklist-item-governance-observability-contract-transition-proof-2026-05-31.md] [baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-governance-observability-contract.unchecked.snapshot-2026-05-31.md]
- Checklist counters after update:
  - unchecked=0
  - checked=24
- Item-line and counter evidence:
  - .github/requirements/transition-proofs/evidence/checklist-item-governance-observability-line-and-count-2026-05-31.txt

## Command Evidence

1. dotnet build .\\src\\Wip.Runtime\\Wip.Runtime.csproj -c Debug --nologo
- Exit code: 0
- Summary: Build succeeded, 0 Warning(s), 0 Error(s).
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-governance-observability-build-wip-runtime-2026-05-31.txt

2. dotnet test .\\tests\\Wip.Shell.E2E.Tests\\Wip.Shell.E2E.Tests.csproj -c Debug --no-build --nologo --filter "FullyQualifiedName~GovernanceDiagnostics_GivenFailedSessionDecision_EmitsCorrelationLinkedLogsAndArtifactReferencesForOfflineReconstruction"
- Exit code: 0
- Summary: Failed=0, Passed=1, Skipped=0, Total=1.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-governance-observability-test-focused-2026-05-31.txt

## Completion Decision

- The exact checklist line is linked to both a transition-proof and a baseline-witness artifact.
- Runtime behavior proof for failed governance decisions is executable and passing, including correlation continuity and decision artifact references in lifecycle events.
- Build and focused E2E verification for this item are captured under tracked transition-proof evidence artifacts.
