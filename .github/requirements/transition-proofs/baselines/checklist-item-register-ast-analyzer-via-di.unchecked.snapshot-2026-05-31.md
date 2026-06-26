# Checklist Baseline Snapshot

Checklist item:
- Register AST analyzer via DI with explicit ownership/lifetime expectations and verify runtime resolver behavior [depends on analyzer implementation]

## Baseline Unchecked Source Text Evidence

Captured at: 2026-05-31T20:42:33.9362786-03:00
Source: .github/requirements/Wip.Validation.DotNet.md

Observed unchecked baseline line witness:

```text
baseline_line=- [ ] Register AST analyzer via DI with explicit ownership/lifetime expectations and verify runtime resolver behavior [depends on analyzer implementation]
baseline_line_sha256=d6be50e2dc041e3fbfa29870fe80a7a0e950314891c3920916b1d021a067ecb6
```

Checklist counters at capture time:

```text
unchecked=2
checked=7
```

Evidence source:
- .github/requirements/transition-proofs/evidence/checklist-item-register-ast-analyzer-via-di-line-transition-2026-05-31.txt
- .github/requirements/transition-proofs/evidence/checklist-item-register-ast-analyzer-via-di-status-2026-05-31.txt

Capture command:

```powershell
$path = '.\.github\requirements\Wip.Validation.DotNet.md'
$itemCore = 'Register AST analyzer via DI with explicit ownership/lifetime expectations and verify runtime resolver behavior [depends on analyzer implementation]'
$baselineLine = '- [ ] ' + $itemCore
$lineMatch = Select-String -Path $path -Pattern ([regex]::Escape($itemCore)) | Select-Object -First 1
$unchecked = (Select-String -Path $path -Pattern '^- \[ \]' | Measure-Object).Count
$checked = (Select-String -Path $path -Pattern '^- \[x\]' | Measure-Object).Count
```