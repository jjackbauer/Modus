# Transition Proof

- Checklist item: Extend `DotNetValidationRequest` with AST options (`EnableAstValidation`, `AstProjectPath`, `AstTimeout`) and validate input guards [depends on AST contracts]
- Date: 2026-05-31
- Scope: one-item repair round closure for DotNet validation request AST options and input guards

## Baseline Unchecked Source Text Evidence

- Baseline witness artifact:
  - .github/requirements/transition-proofs/baselines/checklist-item-dotnetvalidationrequest-ast-options-input-guards.unchecked.snapshot-2026-05-31.md
- Deterministic unchecked baseline line witness:
  - - [ ] Extend `DotNetValidationRequest` with AST options (`EnableAstValidation`, `AstProjectPath`, `AstTimeout`) and validate input guards [depends on AST contracts]
- Unchecked baseline line SHA256:
  - ded1c7787b05c1dc271b1b8a21225f1c5ce667cef63c47cb7e2f59fdae59759e

## Checked Completion Evidence

- Requirements locator: .github/requirements/Wip.Validation.DotNet.md:83
- Checked checklist line:
  - - [x] Extend `DotNetValidationRequest` with AST options (`EnableAstValidation`, `AstProjectPath`, `AstTimeout`) and validate input guards [depends on AST contracts] [transition-proof: .github/requirements/transition-proofs/checklist-item-dotnetvalidationrequest-ast-options-input-guards-transition-proof-2026-05-31.md] [baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-dotnetvalidationrequest-ast-options-input-guards.unchecked.snapshot-2026-05-31.md]
- Checked line SHA256:
  - 1a481423b702a6519cd81c32fb4ac171b71c6da5c1cf26a681c6bf74292c6b0e
- Checklist counters after completion:
  - unchecked=6
  - checked=3

## Command Evidence

1. dotnet build .\\src\\Wip.Validation.DotNet\\Wip.Validation.DotNet.csproj -c Debug --nologo
- Exit code: 0
- Summary: Build succeeded, 0 Warning(s), 0 Error(s), Time Elapsed 00:00:00.92.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-dotnetvalidationrequest-ast-options-input-guards-build-2026-05-31.txt

2. dotnet test .\\tests\\Wip.Validation.DotNet.Tests\\Wip.Validation.DotNet.Tests.csproj -c Debug --no-build --nologo
- Exit code: 0
- Summary: Failed=0, Passed=14, Skipped=0, Total=14, Duration=2s.
- Full log artifact: .github/requirements/transition-proofs/evidence/checklist-item-dotnetvalidationrequest-ast-options-input-guards-test-no-build-2026-05-31.txt

3. Item status and exact line transition evidence commands
- Exit code: 0
- Summary: line captured with checked form, checklist counters captured, and unchecked/checked line SHA256 values persisted.
- Full log artifacts:
  - .github/requirements/transition-proofs/evidence/checklist-item-dotnetvalidationrequest-ast-options-input-guards-status-2026-05-31.txt
  - .github/requirements/transition-proofs/evidence/checklist-item-dotnetvalidationrequest-ast-options-input-guards-line-transition-2026-05-31.txt

## Why This Proof Does Not Depend on Git History

The requirements file for this item is untracked in HEAD, so git history cannot provide a prior committed [ ] state. This proof uses deterministic source-text witnesses (exact unchecked and checked lines plus SHA256 hashes), linked baseline/transition artifacts, and reproducible command logs under .github/requirements/transition-proofs/evidence.
