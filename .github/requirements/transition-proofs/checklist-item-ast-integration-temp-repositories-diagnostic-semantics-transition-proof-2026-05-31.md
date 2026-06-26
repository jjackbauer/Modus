# Transition Proof

- Checklist item: Add integration tests using temporary repositories/projects that prove AST analysis executes against real C# files and propagates diagnostic semantics [depends on all implementation items]
- Date: 2026-05-31
- Scope: one-item repair round closure for item-specific non-VCS transition proof

## Baseline Unchecked Source Text Evidence

- Baseline witness artifact:
  - .github/requirements/transition-proofs/baselines/checklist-item-ast-integration-temp-repositories-diagnostic-semantics.unchecked.snapshot-2026-05-31.md
- Deterministic unchecked baseline line witness:
  - - [ ] Add integration tests using temporary repositories/projects that prove AST analysis executes against real C# files and propagates diagnostic semantics [depends on all implementation items]
- Unchecked baseline line SHA256:
  - e1ed4a9e77594c0cc08c23be7a45b38fbde78a4e70aed8ae9bd7f3fb79bcb740

## Checked Completion Evidence

- Requirements locator: .github/requirements/Wip.Validation.DotNet.md:88
- Checked checklist line:
  - - [x] Add integration tests using temporary repositories/projects that prove AST analysis executes against real C# files and propagates diagnostic semantics [depends on all implementation items] [transition-proof: .github/requirements/transition-proofs/checklist-item-ast-integration-temp-repositories-diagnostic-semantics-transition-proof-2026-05-31.md] [baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-ast-integration-temp-repositories-diagnostic-semantics.unchecked.snapshot-2026-05-31.md]
- Checked line SHA256:
  - 4d969a069f4f16a91e534aad713b38a7aad6e9fe183094dbfe965c227d119506
- Checklist counters after completion:
  - unchecked=1
  - checked=8

## Runtime Test Evidence

- Primary test: ExecuteAsync_GivenTempRepositoryWithValidAndInvalidSources_ProducesExpectedAstDiagnosticSet
  - File: tests/Wip.Validation.DotNet.Tests/DotNet/DotNetValidationValidatorTests.cs
  - Runtime proof focus: real temporary repository with valid/invalid C# files yields deterministic AST diagnostic semantics.
- Related tests (still passing):
  - ExecuteAsync_GivenSequentialValidationRuns_ProducesStableAstResultsForEquivalentInputs
  - ExecuteAsync_GivenAstFailureOnSecondRun_PreservesIsolationAndDoesNotLeakPreviousRunDiagnostics
  - ExecuteAsync_GivenAstValidationEnabled_RunsBuildTestThenAstAndReturnsUnifiedReport

## Command Evidence

1. dotnet build .\\src\\Wip.Validation.DotNet\\Wip.Validation.DotNet.csproj -c Debug --nologo
- Exit code: 0
- Summary: Build succeeded, 0 Warning(s), 0 Error(s).
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-ast-integration-temp-repositories-diagnostic-semantics-build-2026-05-31.txt

2. dotnet test .\\tests\\Wip.Validation.DotNet.Tests\\Wip.Validation.DotNet.Tests.csproj -c Debug --no-build --nologo
- Exit code: 0
- Summary: Failed=0, Passed=32, Skipped=0, Total=32.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-ast-integration-temp-repositories-diagnostic-semantics-test-no-build-2026-05-31.txt

3. Item status and exact line transition evidence commands
- Exit code: 0
- Summary: explicit unchecked baseline witness and checked completion line were captured with SHA256 values and counters.
- Full log artifacts:
  - .github/requirements/transition-proofs/evidence/checklist-item-ast-integration-temp-repositories-diagnostic-semantics-line-transition-2026-05-31.txt
  - .github/requirements/transition-proofs/evidence/checklist-item-ast-integration-temp-repositories-diagnostic-semantics-status-2026-05-31.txt

## Why This Proof Does Not Depend on Git History

The item transition is proven using deterministic source-text witnesses (unchecked and checked lines), SHA256 hashes, linked baseline/transition artifacts, and reproducible command logs under .github/requirements/transition-proofs/evidence.

*All assumptions verified by executable tests and deterministic transition evidence. Zero Falsified rows.*