# Checklist Transition Proof

Checklist item:
- Expand artifact listing guarantees so `artifacts` returns stable descriptor fields (`id`, `type`, `version`, `created`, `producer`, `path`) with deterministic sort order [depends on AS-005 and AS-006 artifact descriptor contract]

## Baseline Unchecked Source Text Evidence

Baseline witness file:
- .github/requirements/transition-proofs/baselines/checklist-item-artifact-listing-guarantees.unchecked.snapshot-2026-05-31.md

Baseline capture:

```text
timestamp=2026-05-31T07:06:01-03:00
line=76:- [ ] Expand artifact listing guarantees so `artifacts` returns stable descriptor fields (`id`, `type`, `version`, `created`, `producer`, `path`) with deterministic sort order [depends on AS-005 and AS-006 artifact descriptor contract]
unchecked=5
checked=19
```

## Checked Completion Evidence

Checked capture:

```text
timestamp=2026-05-31T07:06:15-03:00
line=76:- [x] Expand artifact listing guarantees so `artifacts` returns stable descriptor fields (`id`, `type`, `version`, `created`, `producer`, `path`) with deterministic sort order [depends on AS-005 and AS-006 artifact descriptor contract] [transition-proof: .github/requirements/transition-proofs/checklist-item-artifact-listing-guarantees-transition-proof-2026-05-31.md] [baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-artifact-listing-guarantees.unchecked.snapshot-2026-05-31.md]
unchecked=4
checked=20
```

## Item-Specific Runtime Proof Tests

Updated and executed tests tagged with this checklist item:
- tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - ArtifactsCommand_GivenSessionArtifacts_ReturnsDeterministicDescriptorFieldsAndStableOrdering

## Command Evidence

Build command:

```text
dotnet build .\src\Wip.Runtime\Wip.Runtime.csproj -c Debug --nologo
Build succeeded in 0.9s
```

Focused item-test command:

```text
dotnet test .\tests\Wip.Shell.E2E.Tests\Wip.Shell.E2E.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~ArtifactsCommand_GivenSessionArtifacts_ReturnsDeterministicDescriptorFieldsAndStableOrdering"
Test summary: total: 1, failed: 0, succeeded: 1, skipped: 0
Build succeeded in 2.8s
```

Full target test project command:

```text
dotnet test .\tests\Wip.Shell.E2E.Tests\Wip.Shell.E2E.Tests.csproj -c Debug --no-build --nologo
Test summary: total: 60, failed: 0, succeeded: 60, skipped: 0, duration: 81.1s
Build succeeded in 81.3s
```

## Implementation and Behavioral Delta

Runtime command behavior:
- src/Wip.Shell/Interactive/WipShellCommandLoop.cs
  - `artifacts` output includes stable descriptor fields in deterministic line format: `id`, `type`, `version`, `created`, `producer`, and `path`.
  - Output ordering is deterministic: `created`, `id`, `type`, `version`, `producer`, then `path`.

Behavior-proof test coverage:
- tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - Added checklist binding constant for this exact item.
  - Added `ArtifactsCommand_GivenSessionArtifacts_ReturnsDeterministicDescriptorFieldsAndStableOrdering` to parse and assert required descriptor fields and stable ordering.
