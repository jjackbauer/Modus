# Deterministic fresh-holdout aggregation: baseline, floor 0, chosen floor on the same seeds.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $root '../../..')
$chosenPath = Join-Path $repo '.tmp/goal-floor/results-holdout.jsonl'
$floor0Path = Join-Path $repo '.tmp/goal-floor/results-holdout-floor0.jsonl'
$selectionPath = Join-Path $root '../round-2/floor-selection.json'
$chosenRows = Get-Content -LiteralPath $chosenPath | ForEach-Object { $_ | ConvertFrom-Json }
$floor0All = Get-Content -LiteralPath $floor0Path | ForEach-Object { $_ | ConvertFrom-Json }
$selection = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json

function Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0.0 }
    $middle = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}
function PerSeedMedians($rows) {
    $map = @{}
    foreach ($seed in @($rows | Select-Object -ExpandProperty seed -Unique)) {
        $map[[double]$seed] = Median @($rows | Where-Object { $_.seed -eq $seed } | ForEach-Object { [double]$_.divergenceRate })
    }
    $map
}

$baselineRows = @($floor0All | Where-Object { $_.arm -eq 'baseline' })
$floor0Rows = @($floor0All | Where-Object { $_.arm -eq 'refinement' })
$refinementRows = @($chosenRows | Where-Object { $_.arm -eq 'refinement' })
$seeds = @($baselineRows | Select-Object -ExpandProperty seed -Unique | Sort-Object)
$baseMedians = PerSeedMedians $baselineRows
$refMedians = PerSeedMedians $refinementRows
$floor0Medians = PerSeedMedians $floor0Rows

$baseMedian = Median @($baselineRows | ForEach-Object { [double]$_.divergenceRate })
$refMedian = Median @($refinementRows | ForEach-Object { [double]$_.divergenceRate })
$baseMean = ($baselineRows | Measure-Object divergenceRate -Average).Average
$refMean = ($refinementRows | Measure-Object divergenceRate -Average).Average
$pooledRatio = $refMean / $baseMean

$ratiosVsBaseline = @()
$ratiosVsFloor0 = @()
$perSeed = @()
$substitutions = 0
foreach ($seed in $seeds) {
    $bm = $baseMedians[[double]$seed]
    $rm = $refMedians[[double]$seed]
    $f0 = $floor0Medians[[double]$seed]
    $substitutedBaseline = $bm -eq 0
    if ($substitutedBaseline) { $substitutions++; $rb = $pooledRatio } else { $rb = $rm / $bm }
    $rf = if ($f0 -eq 0) { $null } else { $rm / $f0 }
    $ratiosVsBaseline += $rb
    if ($null -ne $rf) { $ratiosVsFloor0 += $rf }
    $perSeed += [pscustomobject]@{
        seed = [double]$seed
        baseline = $bm
        floor0 = $f0
        chosen = $rm
        ratioVsBaseline = [math]::Round($rb, 6)
        substitutedPooledMean = $substitutedBaseline
        ratioVsFloor0 = if ($null -eq $rf) { $null } else { [math]::Round($rf, 6) }
    }
}
$floor0Median = Median @($floor0Rows | ForEach-Object { [double]$_.divergenceRate })
$baseZero = @($baselineRows | Where-Object { $_.zeroEffect }).Count
$refZero = @($refinementRows | Where-Object { $_.zeroEffect }).Count
$floor0Zero = @($floor0Rows | Where-Object { $_.zeroEffect }).Count
$medianRatioVsBaseline = Median $ratiosVsBaseline
$medianRatioVsFloor0 = Median $ratiosVsFloor0

$random = New-Object System.Random 20261108
$bootstrap = New-Object 'System.Collections.Generic.List[double]'
for ($i = 0; $i -lt 10000; $i++) {
    $sample = New-Object 'System.Collections.Generic.List[double]'
    for ($j = 0; $j -lt $ratiosVsBaseline.Count; $j++) { $sample.Add([double]$ratiosVsBaseline[$random.Next(0, $ratiosVsBaseline.Count)]) }
    $bootstrap.Add((Median $sample.ToArray()))
}
$sorted = @($bootstrap | Sort-Object)
$lower = $sorted[[int][math]::Floor(0.025 * $sorted.Count)]
$upper = $sorted[[int][math]::Floor(0.975 * $sorted.Count)]

$chosen = [double]$selection.chosenFloor
$success = ($medianRatioVsBaseline -ge 1.5) -and ($lower -gt 1.0) -and
    (($refZero / $refinementRows.Count) -le ($baseZero / $baselineRows.Count)) -and
    ($medianRatioVsFloor0 -ge 0.9) -and
    (($refZero / $refinementRows.Count) -le ($floor0Zero / $floor0Rows.Count))

$summary = [pscustomobject]@{
    chosenFloor = $chosen
    rows = $chosenRows.Count + $floor0All.Count
    baselineRows = $baselineRows.Count
    refinementRows = $refinementRows.Count
    floor0Rows = $floor0Rows.Count
    baselineMedianDivergence = [math]::Round($baseMedian, 6)
    floor0MedianDivergence = [math]::Round($floor0Median, 6)
    refinementMedianDivergence = [math]::Round($refMedian, 6)
    medianPerSeedRatioVsBaseline = [math]::Round($medianRatioVsBaseline, 6)
    pooledMeanRatioVsBaseline = [math]::Round($pooledRatio, 6)
    zeroBaselineMedianSubstitutions = $substitutions
    medianPerSeedRatioVsFloor0 = [math]::Round($medianRatioVsFloor0, 6)
    bootstrap95Lower = [math]::Round($lower, 6)
    bootstrap95Upper = [math]::Round($upper, 6)
    baselineZeroEffect = $baseZero
    floor0ZeroEffect = $floor0Zero
    refinementZeroEffect = $refZero
    baselineZeroEffectRate = [math]::Round($baseZero / $baselineRows.Count, 6)
    floor0ZeroEffectRate = [math]::Round($floor0Zero / $floor0Rows.Count, 6)
    refinementZeroEffectRate = [math]::Round($refZero / $refinementRows.Count, 6)
    successRuleMet = $success
    perSeed = $perSeed
}

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $root 'holdout-summary.json'), (($summary | ConvertTo-Json -Depth 8) + "`r`n"), $utf8)
$lines = @('# Fresh holdout — chosen signal floor', '', '| Metric | Baseline | Floor 0 | Chosen floor |', '|---|---:|---:|---:|')
$lines += "| Median divergence | $($summary.baselineMedianDivergence) | $($summary.floor0MedianDivergence) | $($summary.refinementMedianDivergence) |"
$lines += "| Zero-effect | $($summary.baselineZeroEffect)/$($summary.baselineRows) | $($summary.floor0ZeroEffect)/$($summary.floor0Rows) | $($summary.refinementZeroEffect)/$($summary.refinementRows) |"
$lines += ''
$lines += "Chosen floor: **$chosen**"
$lines += "Median per-seed ratio vs baseline: **$($summary.medianPerSeedRatioVsBaseline)** (pooled mean ratio $($summary.pooledMeanRatioVsBaseline); zero-baseline substitutions: $($summary.zeroBaselineMedianSubstitutions))"
$lines += "Bootstrap 95% CI (vs baseline): **[$($summary.bootstrap95Lower), $($summary.bootstrap95Upper)]**"
$lines += "Median per-seed ratio vs floor 0: **$($summary.medianPerSeedRatioVsFloor0)** (non-inferiority threshold 0.9)"
$lines += "Success rule met: **$success**"
[System.IO.File]::WriteAllText((Join-Path $root 'holdout-summary.md'), (($lines -join "`r`n") + "`r`n"), $utf8)
$summary | ConvertTo-Json -Depth 8
