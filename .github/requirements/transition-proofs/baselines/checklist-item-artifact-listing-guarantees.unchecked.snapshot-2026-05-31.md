# Checklist Baseline Snapshot

Checklist item:
- Expand artifact listing guarantees so `artifacts` returns stable descriptor fields (`id`, `type`, `version`, `created`, `producer`, `path`) with deterministic sort order [depends on AS-005 and AS-006 artifact descriptor contract]

## Baseline Unchecked Source Text Evidence

Captured at: 2026-05-31T07:06:01-03:00
Source: .github/requirements/Wip.Next-Steps.md

Observed line:

```text
line=76:- [ ] Expand artifact listing guarantees so `artifacts` returns stable descriptor fields (`id`, `type`, `version`, `created`, `producer`, `path`) with deterministic sort order [depends on AS-005 and AS-006 artifact descriptor contract]
```

Checklist counters at capture time:

```text
unchecked=5
checked=19
```

Capture command:

```powershell
$path = '.\.github\requirements\Wip.Next-Steps.md'
$item = 'Expand artifact listing guarantees so `artifacts` returns stable descriptor fields (`id`, `type`, `version`, `created`, `producer`, `path`) with deterministic sort order [depends on AS-005 and AS-006 artifact descriptor contract]'
$timestamp = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK')
"timestamp=$timestamp"
Select-String -Path $path -SimpleMatch $item | ForEach-Object { "line=$($_.LineNumber):$($_.Line.Trim())" }
$unchecked = (Select-String -Path $path -Pattern '^- \[ \]' ).Count
$checked = (Select-String -Path $path -Pattern '^- \[x\]' ).Count
"unchecked=$unchecked"
"checked=$checked"
```
