# Deterministic DEV floor selection. Reads raw rows only; applies the pre-registered rule.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $root '../../..')
$baseFile = Join-Path $repo '.tmp/goal-refinement/experiment/results.jsonl'

function Read-Rows([string]$path) {
    Get-Content -LiteralPath $path | ForEach-Object { $_ | ConvertFrom-Json }
}
function Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0.0 }
    $middle = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}
function Stats($rows) {
    $rates = @($rows | ForEach-Object { [double]$_.divergenceRate })
    [pscustomobject]@{
        rows = $rows.Count
        medianDivergence = [math]::Round((Median $rates), 6)
        meanDivergence = [math]::Round((($rows | Measure-Object divergenceRate -Average).Average), 6)
        zeroEffect = @($rows | Where-Object { $_.zeroEffect }).Count
        zeroEffectRate = [math]::Round((@($rows | Where-Object { $_.zeroEffect }).Count) / $rows.Count, 6)
        promotions = @($rows | Where-Object { $_.promoted }).Count
    }
}
function PerSeedMedians($rows) {
    $map = @{}
    foreach ($seed in @($rows | Select-Object -ExpandProperty seed -Unique)) {
        $map[[double]$seed] = Median @($rows | Where-Object { $_.seed -eq $seed } | ForEach-Object { [double]$_.divergenceRate })
    }
    $map
}

$all = Read-Rows $baseFile
$baselineRows = @($all | Where-Object { $_.arm -eq 'baseline' })
$floor0Rows = @($all | Where-Object { $_.arm -eq 'refinement' })
$baseStats = Stats $baselineRows
$floor0Stats = Stats $floor0Rows
$baseMedians = PerSeedMedians $baselineRows
$floor0Medians = PerSeedMedians $floor0Rows

$files = [ordered]@{
    '0.05' = Join-Path $repo '.tmp/goal-floor/results-floor005.jsonl'
    '0.10' = Join-Path $repo '.tmp/goal-floor/results-floor010.jsonl'
    '0.20' = Join-Path $repo '.tmp/goal-floor/results-floor020.jsonl'
}
$floors = @()
foreach ($key in $files.Keys) {
    if (-not (Test-Path $files[$key])) { throw "Missing floor rows: $($files[$key])" }
    $rows = @(Read-Rows $files[$key] | Where-Object { $_.arm -eq 'refinement' })
    $stats = Stats $rows
    $medians = PerSeedMedians $rows
    $ratios = @()
    foreach ($seed in @($baseMedians.Keys | Sort-Object)) {
        $ratios += ($medians[$seed] / $baseMedians[$seed])
    }
    $floor0Ratios = @()
    foreach ($seed in @($floor0Medians.Keys | Sort-Object)) {
        $floor0Ratios += ($medians[$seed] / $floor0Medians[$seed])
    }
    $floors += [pscustomobject]@{
        floor = [double]$key
        stats = $stats
        medianRatioVsBaseline = [math]::Round((Median $ratios), 6)
        medianRatioVsFloor0 = [math]::Round((Median $floor0Ratios), 6)
    }
}

$candidates = @($floors | Where-Object {
    $_.stats.zeroEffectRate -le $floor0Stats.zeroEffectRate -and
    $_.stats.medianDivergence -ge (0.9 * $floor0Stats.medianDivergence)
})
$chosen = if ($candidates.Count -gt 0) { ($candidates | Sort-Object floor | Select-Object -First 1).floor } else { 0.0 }

$summary = [pscustomobject]@{
    devSeedCount = 6
    baseline = $baseStats
    floor0 = $floor0Stats
    floors = $floors
    selectionRule = 'smallest positive floor with zero-effect <= floor0 and median divergence >= 0.9x floor0; else 0'
    chosenFloor = $chosen
    candidates = @($candidates | ForEach-Object { $_.floor })
}

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $root 'floor-selection.json'), (($summary | ConvertTo-Json -Depth 8) + "`r`n"), $utf8)

$lines = @('# DEV floor selection', '', '| Floor | Median divergence | Mean divergence | Zero-effect | Promotions | Median ratio vs baseline | Median ratio vs floor 0 |', '|---|---:|---:|---:|---:|---:|---:|')
$lines += "| 0.00 | $($floor0Stats.medianDivergence) | $($floor0Stats.meanDivergence) | $($floor0Stats.zeroEffect)/$($floor0Stats.rows) | $($floor0Stats.promotions) | 1.0 | 1.0 |"
foreach ($f in $floors) {
    $lines += "| $($f.floor) | $($f.stats.medianDivergence) | $($f.stats.meanDivergence) | $($f.stats.zeroEffect)/$($f.stats.rows) | $($f.stats.promotions) | $($f.medianRatioVsBaseline) | $($f.medianRatioVsFloor0) |"
}
$lines += ''
$lines += "Baseline median divergence: **$($baseStats.medianDivergence)**; zero-effect $($baseStats.zeroEffect)/$($baseStats.rows)."
$lines += ''
$lines += "**Chosen floor: $chosen** (candidates: $(@($summary.candidates) -join ', '))."
[System.IO.File]::WriteAllText((Join-Path $root 'floor-selection.md'), (($lines -join "`r`n") + "`r`n"), $utf8)
$summary | ConvertTo-Json -Depth 8
