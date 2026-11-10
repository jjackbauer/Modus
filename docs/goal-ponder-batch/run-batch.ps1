<#
    Runs the pre-registered aggressive batch (10 independent seeds, 4 rounds, 6 seats,
    LLM seed, 378-candidate search at 50 seeds, frozen gate at 24 untouched seeds).
    Sequential. A failed run is retried once with the same seed into a -retry directory.
    All outcomes are appended to docs/goal-ponder-batch/logs/batch-runs.log.
#>
param(
    [int]$StartRun = 1,
    [int]$EndRun = 10
)

$ErrorActionPreference = 'Stop'
$env:THOLDEM_LIVE_MODEL = '1'

$seeds = @(
    1099513000001, 1099513000002, 1099513000003, 1099513000004, 1099513000005,
    1099513000006, 1099513000007, 1099513000008, 1099513000009, 1099513000010
)
$exe = (Resolve-Path '.tmp\goal-llm-harness\loop\bin\Release\net10.0\Loop.dll').Path
$batchRoot = Join-Path (Get-Location) '.tmp\goal-llm-harness\batch'
$logDir = Join-Path (Get-Location) 'docs\goal-ponder-batch\logs'
New-Item -ItemType Directory -Force -Path $batchRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$batchLog = Join-Path $logDir 'batch-runs.log'

for ($run = $StartRun; $run -le $EndRun; $run++) {
    $seed = $seeds[$run - 1]
    $outName = 'run-{0:D2}' -f $run
    $out = Join-Path $batchRoot $outName
    $report = Join-Path $out 'strategies-report.json'
    if (Test-Path $report) {
        $existing = Get-Content $report -Raw | ConvertFrom-Json
        if ($existing.completedRounds -eq 4) {
            Add-Content $batchLog ("run={0} seed={1} status=already-complete out={2}" -f $run, $seed, $outName)
            continue
        }
    }

    $attempt = 0
    $done = $false
    while (-not $done -and $attempt -lt 2) {
        $attempt++
        $suffix = if ($attempt -eq 1) { '' } else { '-retry' }
        $target = "$out$suffix"
        $runLog = Join-Path $logDir ('run-{0:D2}-attempt{1}.log' -f $run, $attempt)
        Write-Output ("starting run={0} seed={1} attempt={2} out={3}" -f $run, $seed, $attempt, $target)
        dotnet $exe --live --secret-file .secrets\deepseek.env --seed $seed --rounds 4 --seats 6 --hands 20 --eval-seeds 24 --eval-hands 20 --refiner-analysis genome --search-seeds 50 --aggressive --out $target 2>&1 | Tee-Object -FilePath $runLog
        $code = $LASTEXITCODE
        $completed = 0
        $reportPath = Join-Path $target 'strategies-report.json'
        if (Test-Path $reportPath) {
            $completed = (Get-Content $reportPath -Raw | ConvertFrom-Json).completedRounds
        }
        Add-Content $batchLog ("run={0} seed={1} attempt={2} exit={3} completed={4} out={5}" -f $run, $seed, $attempt, $code, $completed, (Split-Path $target -Leaf))
        if ($code -eq 0 -and $completed -eq 4) {
            $done = $true
        }
    }
}
Write-Output 'batch segment finished'
