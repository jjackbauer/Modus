# Checklist Baseline Snapshot

Checklist item:
- Integrate AST execution into `DotNetValidationValidator.ExecuteAsync` so build/test and AST evidence are produced in one runtime flow [depends on Roslyn analyzer + request contract extension]

## Baseline Unchecked Source Text Evidence

Captured at: 2026-05-31T00:00:00.0000000-03:00
Source: .github/requirements/Wip.Validation.DotNet.md

Observed unchecked baseline line witness:

```text
unchecked_line=- [ ] Integrate AST execution into `DotNetValidationValidator.ExecuteAsync` so build/test and AST evidence are produced in one runtime flow [depends on Roslyn analyzer + request contract extension]
unchecked_line_sha256=db35dbcfd2349bfd4d970f8f7de91ae2ffe6832d9bc1477b5c6ffbb71f95a8dd
```

Checklist counters at capture time:

```text
unchecked=5
checked=4
```

Evidence source:
- .github/requirements/transition-proofs/evidence/checklist-item-integrate-ast-execution-line-transition-2026-05-31.txt
- .github/requirements/transition-proofs/evidence/checklist-item-integrate-ast-execution-status-2026-05-31.txt

Capture command:

```powershell
$path='.\.github\requirements\Wip.Validation.DotNet.md'
$itemCore='Integrate AST execution into `DotNetValidationValidator.ExecuteAsync` so build/test and AST evidence are produced in one runtime flow [depends on Roslyn analyzer + request contract extension]'
$baselineLine='- [ ] ' + $itemCore
$lineMatch=Select-String -Path $path -Pattern ([regex]::Escape($itemCore)) | Select-Object -First 1
$unchecked=(Select-String -Path $path -Pattern '^- \[ \]').Count
$checked=(Select-String -Path $path -Pattern '^- \[x\]').Count
```