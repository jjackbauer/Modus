# Checklist Baseline Snapshot

Checklist item:
- Extend `DotNetValidationRequest` with AST options (`EnableAstValidation`, `AstProjectPath`, `AstTimeout`) and validate input guards [depends on AST contracts]

## Baseline Unchecked Source Text Evidence

Captured at: 2026-05-31T18:16:01.8060045-03:00
Source: .github/requirements/Wip.Validation.DotNet.md

Observed unchecked baseline line witness:

```text
unchecked_line=- [ ] Extend `DotNetValidationRequest` with AST options (`EnableAstValidation`, `AstProjectPath`, `AstTimeout`) and validate input guards [depends on AST contracts]
unchecked_line_sha256=ded1c7787b05c1dc271b1b8a21225f1c5ce667cef63c47cb7e2f59fdae59759e
```

Checklist counters at capture time:

```text
unchecked=6
checked=3
```

Evidence source:
- .github/requirements/transition-proofs/evidence/checklist-item-dotnetvalidationrequest-ast-options-input-guards-line-transition-2026-05-31.txt
- .github/requirements/transition-proofs/evidence/checklist-item-dotnetvalidationrequest-ast-options-input-guards-status-2026-05-31.txt

Capture command:

```powershell
$path='.\.github\requirements\Wip.Validation.DotNet.md'
$itemCore='Extend `DotNetValidationRequest` with AST options (`EnableAstValidation`, `AstProjectPath`, `AstTimeout`) and validate input guards [depends on AST contracts]'
$baselineLine='- [ ] ' + $itemCore
$lineMatch=Select-String -Path $path -Pattern ([regex]::Escape($itemCore)) | Select-Object -First 1
$unchecked=(Select-String -Path $path -Pattern '^- \[ \]').Count
$checked=(Select-String -Path $path -Pattern '^- \[x\]').Count
```
