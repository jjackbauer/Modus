# Deterministic DEV aggregation for the pre-registered divergence experiment.
# Reads raw rows only; writes a summary JSON and Markdown next to this script.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$results = Join-Path $root '../../../.tmp/goal-refinement/experiment/results.jsonl'
$rows = Get-Content -LiteralPath $results | ForEach-Object { $_ | ConvertFrom-Json }

function Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0.0 }
    $middle = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

$seeds = @($rows | Select-Object -ExpandProperty seed -Unique | Sort-Object)
$perSeed = @()
foreach ($seed in $seeds) {
    $baseline = @($rows | Where-Object { $_.arm -eq 'baseline' -and $_.seed -eq $seed })
    $refinement = @($rows | Where-Object { $_.arm -eq 'refinement' -and $_.seed -eq $seed })
    $baseMedian = Median @($baseline | ForEach-Object { [double]$_.divergenceRate })
    $refMedian = Median @($refinement | ForEach-Object { [double]$_.divergenceRate })
    $ratio = if ($baseMedian -eq 0) { $null } else { $refMedian / $baseMedian }
    $perSeed += [pscustomobject]@{
        seed = [double]$seed
        baselineMedian = [math]::Round($baseMedian, 6)
        refinementMedian = [math]::Round($refMedian, 6)
        ratio = if ($null -eq $ratio) { $null } else { [math]::Round($ratio, 6) }
        baselineZeroEffect = @($baseline | Where-Object { $_.zeroEffect }).Count
        refinementZeroEffect = @($refinement | Where-Object { $_.zeroEffect }).Count
    }
}

$baselineRows = @($rows | Where-Object { $_.arm -eq 'baseline' })
$refinementRows = @($rows | Where-Object { $_.arm -eq 'refinement' })
$baseMean = ($baselineRows | Measure-Object divergenceRate -Average).Average
$refMean = ($refinementRows | Measure-Object divergenceRate -Average).Average
$baseZero = @($baselineRows | Where-Object { $_.zeroEffect }).Count
$refZero = @($refinementRows | Where-Object { $_.zeroEffect }).Count

# Paired bootstrap over seeds: resample the six (baseline, refinement) seed pairs.
$ratios = @($perSeed | ForEach-Object { $_.ratio })
$random = New-Object System.Random 20261108
$bootstrap = New-Object 'System.Collections.Generic.List[double]'
for ($iteration = 0; $iteration -lt 10000; $iteration++) {
    $sample = New-Object 'System.Collections.Generic.List[double]'
    for ($draw = 0; $draw -lt $seeds.Count; $draw++) {
        $index = $random.Next(0, $seeds.Count)
        $sample.Add([double]$ratios[$index])
    }
    $bootstrap.Add((Median $sample.ToArray()))
}
$sortedBootstrap = @($bootstrap | Sort-Object)
$lower = $sortedBootstrap[[int][math]::Floor(0.025 * $sortedBootstrap.Count)]
$upper = $sortedBootstrap[[int][math]::Floor(0.975 * $sortedBootstrap.Count)]

$summary = [pscustomobject]@{
    split = 'dev'
    seeds = $seeds.Count
    rows = $rows.Count
    baselineRows = $baselineRows.Count
    refinementRows = $refinementRows.Count
    baselineMeanDivergence = [math]::Round($baseMean, 6)
    refinementMeanDivergence = [math]::Round($refMean, 6)
    pooledMeanRatio = [math]::Round($refMean / $baseMean, 6)
    medianPerSeedRatio = [math]::Round((Median $ratios), 6)
    bootstrap95Lower = [math]::Round($lower, 6)
    bootstrap95Upper = [math]::Round($upper, 6)
    baselineZeroEffect = $baseZero
    refinementZeroEffect = $refZero
    baselineZeroEffectRate = [math]::Round($baseZero / $baselineRows.Count, 6)
    refinementZeroEffectRate = [math]::Round($refZero / $refinementRows.Count, 6)
    baselinePromotions = @($baselineRows | Where-Object { $_.promoted }).Count
    refinementPromotions = @($refinementRows | Where-Object { $_.promoted }).Count
    perSeed = $perSeed
}

$json = $summary | ConvertTo-Json -Depth 6
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $root 'dev-summary.json'), $json + "`r`n", $utf8)

$lines = @('# DEV divergence summary', '', '| Metric | Baseline | Refinement |', '|---|---|---|')
$lines += "| Rows | $($summary.baselineRows) | $($summary.refinementRows) |"
$lines += "| Mean divergence | $($summary.baselineMeanDivergence) | $($summary.refinementMeanDivergence) |"
$lines += "| Zero-effect rows | $($summary.baselineZeroEffect) | $($summary.refinementZeroEffect) |"
$lines += "| Zero-effect rate | $($summary.baselineZeroEffectRate) | $($summary.refinementZeroEffectRate) |"
$lines += "| Verified promotions | $($summary.baselinePromotions) | $($summary.refinementPromotions) |"
$lines += ''
$lines += "Pooled mean ratio: **$($summary.pooledMeanRatio)**"
$lines += "Median per-seed ratio: **$($summary.medianPerSeedRatio)**"
$lines += "Paired bootstrap 95% CI (10,000 resamples, seed 20261108): **[$($summary.bootstrap95Lower), $($summary.bootstrap95Upper)]**"
$lines += ''
$lines += '| Seed | Baseline median | Refinement median | Ratio |'
$lines += '|---|---|---|---|'
foreach ($row in $perSeed) {
    $lines += "| $($row.seed) | $($row.baselineMedian) | $($row.refinementMedian) | $($row.ratio) |"
}
[System.IO.File]::WriteAllText((Join-Path $root 'dev-summary.md'), (($lines -join "`r`n") + "`r`n"), $utf8)
$summary | ConvertTo-Json -Depth 6
