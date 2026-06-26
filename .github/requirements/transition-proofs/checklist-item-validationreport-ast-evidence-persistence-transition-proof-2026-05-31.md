# Transition Proof

- Checklist item: Persist AST evidence inside `ValidationReport` artifact (counts, diagnostics, success state, elapsed window) and keep serialization deterministic [depends on validator AST execution]
- Date: 2026-05-31
- Scope: one-item repair round closure for AST evidence persistence transition proof gap

## Baseline Unchecked Source Text Evidence

- Baseline witness artifact:
  - .github/requirements/transition-proofs/baselines/checklist-item-validationreport-ast-evidence-persistence.unchecked.snapshot-2026-05-31.md
- Deterministic unchecked baseline line witness:
  - - [ ] Persist AST evidence inside `ValidationReport` artifact (counts, diagnostics, success state, elapsed window) and keep serialization deterministic [depends on validator AST execution]
- Unchecked baseline line SHA256:
  - f52c82e8b95b345005af56ee779e2e478e50910ce85ae9d3a42f8c2d16922f78

## Checked Completion Evidence

- Requirements locator: .github/requirements/Wip.Validation.DotNet.md:85
- Checked checklist line:
  - - [x] Persist AST evidence inside `ValidationReport` artifact (counts, diagnostics, success state, elapsed window) and keep serialization deterministic [depends on validator AST execution] [transition-proof: .github/requirements/transition-proofs/checklist-item-validationreport-ast-evidence-persistence-transition-proof-2026-05-31.md] [baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-validationreport-ast-evidence-persistence.unchecked.snapshot-2026-05-31.md]
- Checked line SHA256:
  - 7213aa6785158f1003fd36f821ca380d8cde0a9d01e7ab1d938a377a390e9504
- Checklist counters after completion:
  - unchecked=4
  - checked=5

## Runtime Test Evidence

- Primary tests:
  - ValidationReportArtifact_GivenAstExecution_PersistsAstSectionWithDeterministicJsonShape
  - ValidationReportArtifact_GivenKnownDiagnosticSet_RoundTripsDiagnosticsWithoutSemanticDrift
  - ExecuteAsync_GivenAnalyzerFailure_MarksOverallResultFailedAndPersistsAstFailureEvidence

## Command Evidence

1. dotnet build .\\src\\Wip.Validation.DotNet\\Wip.Validation.DotNet.csproj -c Debug --nologo
- Exit code: 0
- Summary: Build succeeded, 0 Warning(s), 0 Error(s), Time Elapsed 00:00:01.14.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-validationreport-ast-evidence-persistence-build-2026-05-31.txt

2. dotnet test .\\tests\\Wip.Validation.DotNet.Tests\\Wip.Validation.DotNet.Tests.csproj -c Debug --no-build --nologo
- Exit code: 0
- Summary: Failed=0, Passed=19, Skipped=0, Total=19, Duration=4s.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-validationreport-ast-evidence-persistence-test-no-build-2026-05-31.txt

3. Item status and exact line transition evidence commands
- Exit code: 0
- Summary: captured checked line with transition links, deterministic checked/unchecked SHA256 values, and checklist counters.
- Full log artifacts:
  - .github/requirements/transition-proofs/evidence/checklist-item-validationreport-ast-evidence-persistence-line-transition-2026-05-31.txt
  - .github/requirements/transition-proofs/evidence/checklist-item-validationreport-ast-evidence-persistence-status-2026-05-31.txt

## Why This Proof Does Not Depend on Git History

This transition proof is based on deterministic source-text witnesses and reproducible command outputs stored under .github/requirements/transition-proofs. It does not require git history for baseline/checked verification of this checklist line.

*All assumptions verified by executable tests and deterministic transition evidence. Zero Falsified rows.*
