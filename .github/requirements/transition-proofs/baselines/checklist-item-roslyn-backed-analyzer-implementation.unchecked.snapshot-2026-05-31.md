# Baseline Snapshot

- Checklist item baseline witness date: 2026-05-31
- Requirements document: .github/requirements/Wip.Validation.DotNet.md
- Baseline unchecked source text witness (exact item text):
  - - [ ] Add Roslyn-backed analyzer implementation that parses C# documents from a target project path and emits deterministic diagnostic payloads [depends on AST contracts]

## Source Anchors

- tests/Wip.Validation.DotNet.Tests/DotNet/RoslynAstAnalyzerTests.cs
  - AnalyzeAsync_GivenCompilableProject_ReturnsSucceededResultWithParsedSyntaxTrees
  - AnalyzeAsync_GivenKnownSyntaxError_ReturnsFailedResultWithExpectedDiagnosticRuleAndLocation
- src/Wip.Validation.DotNet/DotNet/RoslynAstAnalyzer.cs
  - RoslynAstAnalyzer.AnalyzeAsync

## Deterministic Witness

- Unchecked line SHA256:
  - df77e254e3bfd0dac96ba5567de9173083e533e1064f96f901f8b51d3b9385f9
