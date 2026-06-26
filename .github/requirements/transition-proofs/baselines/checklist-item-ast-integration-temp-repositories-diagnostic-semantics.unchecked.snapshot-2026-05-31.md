# Checklist Baseline Snapshot

Checklist item:
- Add integration tests using temporary repositories/projects that prove AST analysis executes against real C# files and propagates diagnostic semantics [depends on all implementation items]

## Baseline Unchecked Source Text Evidence

Captured at: 2026-05-31
Source: .github/requirements/Wip.Validation.DotNet.md

Observed unchecked baseline line witness:

```text
unchecked_line=- [ ] Add integration tests using temporary repositories/projects that prove AST analysis executes against real C# files and propagates diagnostic semantics [depends on all implementation items]
unchecked_line_sha256=e1ed4a9e77594c0cc08c23be7a45b38fbde78a4e70aed8ae9bd7f3fb79bcb740
```

Checklist counters at capture time:

```text
unchecked=1
checked=8
```

Evidence source:
- .github/requirements/transition-proofs/evidence/checklist-item-ast-integration-temp-repositories-diagnostic-semantics-line-transition-2026-05-31.txt
- .github/requirements/transition-proofs/evidence/checklist-item-ast-integration-temp-repositories-diagnostic-semantics-status-2026-05-31.txt

Capture command:

```powershell
$path='.\.github\requirements\Wip.Validation.DotNet.md'
$itemCore='Add integration tests using temporary repositories/projects that prove AST analysis executes against real C# files and propagates diagnostic semantics [depends on all implementation items]'
$uncheckedLine='- [ ] ' + $itemCore
$checkedLine=(Select-String -Path $path -Pattern '^- \[x\] Add integration tests using temporary repositories/projects that prove AST analysis executes against real C# files and propagates diagnostic semantics \[depends on all implementation items\]' | Select-Object -First 1).Line.Trim()
$unchecked=(Select-String -Path $path -Pattern '^- \[ \]' | Measure-Object).Count
$checked=(Select-String -Path $path -Pattern '^- \[x\]' | Measure-Object).Count
```