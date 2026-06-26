# Checklist Baseline Snapshot

Checklist item:
- Persist AST evidence inside `ValidationReport` artifact (counts, diagnostics, success state, elapsed window) and keep serialization deterministic [depends on validator AST execution]

## Baseline Unchecked Source Text Evidence

Captured at: 2026-05-31T20:28:26.5031107-03:00
Source: .github/requirements/Wip.Validation.DotNet.md

Observed unchecked baseline line witness:

```text
unchecked_line=- [ ] Persist AST evidence inside `ValidationReport` artifact (counts, diagnostics, success state, elapsed window) and keep serialization deterministic [depends on validator AST execution]
unchecked_line_sha256=f52c82e8b95b345005af56ee779e2e478e50910ce85ae9d3a42f8c2d16922f78
```

Checklist counters at capture time:

```text
unchecked=4
checked=5
```

Evidence source:
- .github/requirements/transition-proofs/evidence/checklist-item-validationreport-ast-evidence-persistence-line-transition-2026-05-31.txt
- .github/requirements/transition-proofs/evidence/checklist-item-validationreport-ast-evidence-persistence-status-2026-05-31.txt

Capture command:

```powershell
$path='.\.github\requirements\Wip.Validation.DotNet.md'
$itemCore='Persist AST evidence inside `ValidationReport` artifact (counts, diagnostics, success state, elapsed window) and keep serialization deterministic [depends on validator AST execution]'
$baselineLine='- [ ] ' + $itemCore
$lineMatch=Select-String -Path $path -Pattern ([regex]::Escape($itemCore)) | Select-Object -First 1
$unchecked=(Select-String -Path $path -Pattern '^- \[ \]' | Measure-Object).Count
$checked=(Select-String -Path $path -Pattern '^- \[x\]' | Measure-Object).Count
```
