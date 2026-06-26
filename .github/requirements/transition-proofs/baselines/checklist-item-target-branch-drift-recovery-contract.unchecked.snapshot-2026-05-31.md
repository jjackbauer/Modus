# Checklist Baseline Snapshot

Checklist item:
- Add explicit target-branch-drift recovery contract defining required rebase/revalidate/refresh steps and observable session state transitions before retrying merge [depends on AP-005 target drift recovery guidance]

## Baseline Unchecked Source Text Evidence

Captured at: 2026-05-31T07:52:51-03:00
Source: .github/requirements/Wip.Next-Steps.md

Observed unchecked baseline line witness:

```text
unchecked_line=- [ ] Add explicit target-branch-drift recovery contract defining required rebase/revalidate/refresh steps and observable session state transitions before retrying merge [depends on AP-005 target drift recovery guidance]
unchecked_line_sha256=cac5cae0afea1fcb8292f3f5d7888e99821fa09a97501d8d8bfd58e16a8df9f5
```

Checklist counters at capture time:

```text
unchecked=1
checked=23
```

Evidence source:
- .github/requirements/transition-proofs/evidence/checklist-item-target-branch-drift-recovery-contract-line-transition-2026-05-31.txt
- .github/requirements/transition-proofs/evidence/checklist-item-target-branch-drift-recovery-contract-status-2026-05-31.txt

Capture command:

```powershell
$path='.\.github\requirements\Wip.Next-Steps.md'
$itemCore='Add explicit target-branch-drift recovery contract defining required rebase/revalidate/refresh steps and observable session state transitions before retrying merge [depends on AP-005 target drift recovery guidance]'
$baselineLine='- [ ] ' + $itemCore
$lineMatch=Select-String -Path $path -Pattern ([regex]::Escape($itemCore)) | Select-Object -First 1
$unchecked=(Select-String -Path $path -Pattern '^- \[ \]').Count
$checked=(Select-String -Path $path -Pattern '^- \[x\]').Count
```
