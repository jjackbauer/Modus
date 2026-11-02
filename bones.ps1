<#
.SYNOPSIS
  Bones Learning Loop launcher
.DESCRIPTION
  Usage: .\bones.ps1 <command>
  Commands: run, resume, cold, stub, status, stop, start, library, recover, test, build
#>

param(
    [Parameter(Position = 0)]
    [ValidateSet("run", "resume", "cold", "stub", "status", "stop", "start",
                 "library", "recover", "reset-library", "test", "test-host", "build", "bench", "help")]
    [string]$Command = "help"
)

$Project   = "WIP/Wip.Bones.Host"
$Recovery  = "WIP/BonesLibraryRecovery"
$DataDir   = "WIP/Wip.Bones.Host/bones-data"
$Library   = "$DataDir/.bones/strategy-library"
$Url       = "http://127.0.0.1:8080"

if (-not $env:BONES_RESUME_FROM_BEST)   { $env:BONES_RESUME_FROM_BEST = "true" }
if (-not $env:BONES_MAX_GAMES_PER_RUN)  { $env:BONES_MAX_GAMES_PER_RUN = "100" }

$maxGamesPerRun = 0
if (-not [int]::TryParse($env:BONES_MAX_GAMES_PER_RUN, [ref]$maxGamesPerRun)) {
    throw "BONES_MAX_GAMES_PER_RUN='$($env:BONES_MAX_GAMES_PER_RUN)' must be an integer."
}

if ($maxGamesPerRun -eq 0) {
    $allowUnbounded = $false
    if ($env:BONES_ALLOW_UNBOUNDED) {
        [void][bool]::TryParse($env:BONES_ALLOW_UNBOUNDED, [ref]$allowUnbounded)
    }

    if (-not $allowUnbounded) {
        throw "BONES_MAX_GAMES_PER_RUN=0 (unlimited) requires explicit BONES_ALLOW_UNBOUNDED=true."
    }
}

switch ($Command) {
    "run" {
        dotnet run --project $Project
    }
    "resume" {
        $env:BONES_RESUME_FROM_BEST = "true"
        $env:BONES_MAX_GAMES_PER_RUN = "100"
        dotnet run --project $Project
    }
    "cold" {
        $env:BONES_RESUME_FROM_BEST = "false"
        $env:BONES_MAX_GAMES_PER_RUN = "100"
        dotnet run --project $Project
    }
    "stub" {
        $env:BonesHost__ModelProvider = "stub"
        $env:BONES_MAX_GAMES_PER_RUN = "10"
        dotnet run --project $Project
    }
    "status" {
        try { Invoke-RestMethod "$Url/api/bones/status" | ConvertTo-Json -Depth 5 }
        catch { Write-Host "Host not running at $Url" -ForegroundColor Yellow }
    }
    "stop" {
        Invoke-RestMethod -Method Post "$Url/api/bones/stop" | ConvertTo-Json
    }
    "start" {
        Invoke-RestMethod -Method Post "$Url/api/bones/start" | ConvertTo-Json
    }
    "library" {
        Get-ChildItem "$Library/seat-*.json" -ErrorAction SilentlyContinue | ForEach-Object {
            $j = Get-Content $_.FullName -Raw | ConvertFrom-Json
            Write-Host "$($j.strategyId) ($($j.kind)) W:$($j.wins)/$($j.matchesPlayed) diff:$($j.cumulativeScoreDifferential)"
        }
    }
    "recover" {
        dotnet run --project $Recovery
    }
    "reset-library" {
        Remove-Item -Recurse -Force $Library -ErrorAction SilentlyContinue
        Write-Host "Library wiped."
    }
    "test" {
        dotnet test WIP/Wip.Bones.Tests --verbosity quiet
    }
    "test-host" {
        dotnet test WIP/Wip.Bones.Host.Tests --verbosity quiet
    }
    "build" {
        dotnet build WIP/Wip.Bones.Agents
        dotnet build WIP/Wip.Bones.Host
        dotnet build $Recovery
    }
    "bench" {
        $Bench = "WIP/Wip.Bones.Benchmark"
        New-Item -ItemType Directory -Force -Path ".tmp/bench" | Out-Null
        dotnet build $Bench
        dotnet run --no-build --project $Bench -- history --data $DataDir --out .tmp/bench/history.json
        dotnet run --no-build --project $Bench -- arena --snapshot .tmp/bench/history.json --out .tmp/bench/dev.json --games 1000 --master-seed 910001 --label dev --target 12
    }
    default {
        Write-Host "Bones Learning Loop"
        Write-Host "  resume       Resume from library"
        Write-Host "  cold         Cold start - re-ponder"
        Write-Host "  stub         Offline stub mode"
        Write-Host "  run          Start with env vars"
        Write-Host "  status       Loop status"
        Write-Host "  stop/start   Control loop"
        Write-Host "  library      Show library"
        Write-Host "  recover      Rebuild library from sessions"
        Write-Host "  reset-library Wipe library"
        Write-Host "  test         Run agent tests"
        Write-Host "  build        Build all projects"
        Write-Host "  bench        Deterministic held-out benchmark (read-only)"
    }
}
