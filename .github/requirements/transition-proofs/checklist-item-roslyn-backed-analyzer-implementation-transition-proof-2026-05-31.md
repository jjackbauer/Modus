# Transition Proof

- Checklist item: Add Roslyn-backed analyzer implementation that parses C# documents from a target project path and emits deterministic diagnostic payloads [depends on AST contracts]
- Date: 2026-05-31
- Scope: repair round transition-proof closure for non-VCS verifiability

## Implementation Evidence

- Production implementation exists in `src/Wip.Validation.DotNet/DotNet/RoslynAstAnalyzer.cs`:
  - `IRoslynAstAnalyzer.AnalyzeAsync`
  - `RoslynAstAnalyzer.AnalyzeAsync`
  - Deterministic diagnostic projection and ordering are part of the analyzer output contract.
- Runtime behavior proofs exist in `tests/Wip.Validation.DotNet.Tests/DotNet/RoslynAstAnalyzerTests.cs`:
  - `AnalyzeAsync_GivenCompilableProject_ReturnsSucceededResultWithParsedSyntaxTrees`
  - `AnalyzeAsync_GivenKnownSyntaxError_ReturnsFailedResultWithExpectedDiagnosticRuleAndLocation`

## Baseline Unchecked Source Text Evidence

- Baseline witness artifact:
  - `.github/requirements/transition-proofs/baselines/checklist-item-roslyn-backed-analyzer-implementation.unchecked.snapshot-2026-05-31.md`
- Deterministic unchecked baseline line witness:
  - `- [ ] Add Roslyn-backed analyzer implementation that parses C# documents from a target project path and emits deterministic diagnostic payloads [depends on AST contracts]`
- Unchecked baseline line SHA256:
  - `df77e254e3bfd0dac96ba5567de9173083e533e1064f96f901f8b51d3b9385f9`

## Checked Completion Evidence

- Requirements locator: `.github/requirements/Wip.Validation.DotNet.md:82`
- Checked checklist line now includes linked transition proof and baseline witness for independent verification.
- Checked checklist line SHA256:
  - `291be0748f676e44db25c4a10dd87aeb86f7d0f659c7122e7a924ad3a5c58c36`
- Checklist counters after completion:
  - unchecked=7
  - checked=2

## Command Evidence

1. `dotnet build .\src\Wip.Validation.DotNet\Wip.Validation.DotNet.csproj -c Debug --nologo`
- Exit code: 0
- Full log artifact: `.github/requirements/transition-proofs/evidence/checklist-item-roslyn-backed-analyzer-implementation-build-2026-05-31.txt`

2. `dotnet test .\tests\Wip.Validation.DotNet.Tests\Wip.Validation.DotNet.Tests.csproj -c Debug --nologo`
- Exit code: 0
- Full log artifact: `.github/requirements/transition-proofs/evidence/checklist-item-roslyn-backed-analyzer-implementation-test-2026-05-31.txt`

3. Checklist status and line-hash capture command
- Exit code: 0
- Full log artifacts:
  - `.github/requirements/transition-proofs/evidence/checklist-item-roslyn-backed-analyzer-implementation-status-2026-05-31.txt`
  - `.github/requirements/transition-proofs/evidence/checklist-item-roslyn-backed-analyzer-implementation-line-transition-2026-05-31.txt`

## Completion Decision

- The exact checklist line is explicitly `[x]` and includes deterministic links to this transition-proof artifact and the baseline witness artifact.
- Independent verification can evaluate the exact unchecked witness text and exact checked line hash without relying on git history for this requirements file.

## Why This Proof Does Not Depend on Git History

This proof intentionally does not require a committed `[ ] -> [x]` history for `.github/requirements/Wip.Validation.DotNet.md`. Instead, it uses deterministic source-text witnesses, stable per-item artifact links under `.github/requirements/transition-proofs/`, line-level SHA256 values, and reproducible build/test logs captured under `.github/requirements/transition-proofs/evidence/`.
