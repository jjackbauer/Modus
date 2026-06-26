# Checklist Baseline Snapshot

Checklist item:
- Enforce approval prerequisite that current review evidence exists for the current diff hash at approval time, with deterministic stale-review rejection guidance [depends on RV-002 and RV-003 absolute review gate]

## Baseline Unchecked Source Text Evidence

Captured at: 2026-05-31T06:50:07-03:00
Source: .github/requirements/Wip.Next-Steps.md

Observed line:

```text
line=75:- [ ] Enforce approval prerequisite that current review evidence exists for the current diff hash at approval time, with deterministic stale-review rejection guidance [depends on RV-002 and RV-003 absolute review gate]
```

Checklist counters at capture time:

```text
unchecked=6
checked=18
```

Capture command:

```powershell
$path = '.\.github\requirements\Wip.Next-Steps.md'
$item = 'Enforce approval prerequisite that current review evidence exists for the current diff hash at approval time, with deterministic stale-review rejection guidance [depends on RV-002 and RV-003 absolute review gate]'
$timestamp = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK')
"timestamp=$timestamp"
Select-String -Path $path -Pattern ([regex]::Escape($item)) | ForEach-Object { "line=$($_.LineNumber):$($_.Line.Trim())" }
$unchecked = (Select-String -Path $path -Pattern '^- \[ \]' ).Count
$checked = (Select-String -Path $path -Pattern '^- \[x\]' ).Count
"unchecked=$unchecked"
"checked=$checked"
```
