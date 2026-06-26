# Checklist Transition Proof

Checklist item:
- Add privileged-merge isolation proof that merge semantics cannot be triggered through generic shell tool execution even under command aliasing attempts [depends on TL-005 privileged merge-only path]

## Baseline Unchecked Source Text Evidence

Baseline witness file:
- .github/requirements/transition-proofs/baselines/checklist-item-privileged-merge-isolation.unchecked.snapshot-2026-05-31.md

Baseline capture:

```text
timestamp=2026-05-31T07:24:04-03:00
line=77:- [ ] Add privileged-merge isolation proof that merge semantics cannot be triggered through generic shell tool execution even under command aliasing attempts [depends on TL-005 privileged merge-only path]
unchecked=4
checked=20
```

## Checked Completion Evidence

Checked capture:

```text
timestamp=2026-05-31T07:24:15-03:00
line=77:- [x] Add privileged-merge isolation proof that merge semantics cannot be triggered through generic shell tool execution even under command aliasing attempts [depends on TL-005 privileged merge-only path] [transition-proof: .github/requirements/transition-proofs/checklist-item-privileged-merge-isolation-transition-proof-2026-05-31.md] [baseline-witness: .github/requirements/transition-proofs/baselines/checklist-item-privileged-merge-isolation.unchecked.snapshot-2026-05-31.md]
unchecked=3
checked=21
```

## Item-Specific Runtime Proof Tests

Updated and executed tests tagged with this checklist item:
- tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - MergeIsolation_GivenGenericShellToolExecutionAttempt_RejectsMergeSemanticsAndPreservesRepositoryState

Alias-attempt cases asserted by the checklist item test:
- `dotnet build --nologo && git -c alias.integrate=merge integrate HEAD`
- `dotnet build --nologo && git -c alias.sync="pull --rebase" sync`
- `dotnet build --nologo && git config alias.integrate merge && git integrate HEAD`

## Command Evidence

Focused item-test command:

```text
dotnet test .\tests\Wip.Shell.E2E.Tests\Wip.Shell.E2E.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~MergeIsolation_GivenGenericShellToolExecutionAttempt_RejectsMergeSemanticsAndPreservesRepositoryState"
Test summary: total: 3, failed: 0, succeeded: 3, skipped: 0, duration: 4.0s
Build succeeded in 6.1s
```

Build command:

```text
dotnet build .\src\Wip.Runtime\Wip.Runtime.csproj -c Debug --nologo
Build succeeded in 0.9s
```

Full target test project command:

```text
dotnet test .\tests\Wip.Shell.E2E.Tests\Wip.Shell.E2E.Tests.csproj -c Debug --no-build --nologo
Test summary: total: 63, failed: 0, succeeded: 63, skipped: 0, duration: 84.5s
Build succeeded in 84.7s
```

## Implementation and Behavioral Delta

Behavior-proof test update:
- tests/Wip.Shell.E2E.Tests/E2E/ShellHostE2EHarnessTests.cs
  - Added a `git config alias.<name> merge` command-aliasing attempt to prove generic shell tool execution remains isolated from privileged merge semantics.

Runtime implementation changes for this item in this iteration:
- None. Existing enforcement remains in `LocalSafePolicy.IsPrivilegedMergeCommandOnGenericToolPath` and `HasPrivilegedMergeAliasDefinition`.