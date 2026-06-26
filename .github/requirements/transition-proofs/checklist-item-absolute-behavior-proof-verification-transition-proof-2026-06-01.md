# Transition Proof - Absolute Behavior-Proof Verification

Date: 2026-06-01
Requirements Document: .github/requirements/Wip.Dynamic-System-Prompt.md
Checklist Item: Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]

## Baseline Unchecked Source Text Evidence

- Original checklist line before this implementation slice: `- [ ] Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]`
- Compliance harness baseline defect: `tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs` read `.github/requirements/Wip.Next-Steps.md` and only executed planned tests from the `Absolute Behavior-Proof Compliance Gate` subsection.
- Baseline plan drift defect: `.github/requirements/Wip.Dynamic-System-Prompt.md` named 19 planned integration tests that did not exist as executable xUnit methods in `tests/Wip.Shell.E2E.Tests/E2E`.

## Checked Completion Evidence

- Compliance harness now reads `.github/requirements/Wip.Dynamic-System-Prompt.md` and builds execution batches from every planned integration test, not only the compliance subsection.
- Requirements test plan now maps to executable runtime-backed tests in `tests/Wip.Shell.E2E.Tests/E2E`, and behavior-proof assumptions were tightened where metadata-only phrasing failed policy anchors.
- Checklist-bound proof remains enforced by `[Trait("ChecklistItem", "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]")]` on the compliance gate tests in `tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs`.
- Focused validation passed: `BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem` and `BehaviorProofCompliance_GivenPlannedNonPolicyIntegrationTests_SelectsThemForExecution` passed together in 12 seconds.
- Project build passed: `dotnet build src/Wip.Shell/Wip.Shell.csproj -c Debug --nologo` completed successfully with 0 warnings and 0 errors.
- Full E2E project evidence was captured with `dotnet test tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj -c Debug --no-build --nologo`, but that command still fails on the separate existing test `PlanProviderInvocation_GivenPromptDiagnostics_ExpectedArtifactPersistenceAndStatusSurfacing`; this failure is outside the behavior-proof compliance slice implemented here.

## Evidence Files

- .github/requirements/transition-proofs/evidence/checklist-item-absolute-behavior-proof-focused-tests-2026-06-01.txt
- .github/requirements/transition-proofs/evidence/checklist-item-absolute-behavior-proof-build-2026-06-01.txt
- .github/requirements/transition-proofs/evidence/checklist-item-absolute-behavior-proof-e2e-tests-2026-06-01.txt