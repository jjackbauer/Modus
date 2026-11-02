param(
    [string] $SolutionPath = 'Wip.slnx'
)

$ErrorActionPreference = 'Stop'

$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) {
    throw 'Not inside a git repository.'
}

$solutionFullPath = Join-Path $repoRoot $SolutionPath
if (-not (Test-Path -LiteralPath $solutionFullPath)) {
    throw "Solution not found: $solutionFullPath"
}

[xml] $solution = Get-Content -Raw -LiteralPath $solutionFullPath
$solutionProjects = @(
    $solution.SelectNodes('//Project') |
        ForEach-Object { $_.Path.Replace('\', '/') } |
        Sort-Object -Unique
)

$wipRoot = Join-Path $repoRoot 'WIP'
$wipProjects = @(
    Get-ChildItem -LiteralPath $wipRoot -Recurse -Filter '*.csproj' |
        ForEach-Object {
            $_.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        } |
        Sort-Object -Unique
)

$missing = @($wipProjects | Where-Object { $solutionProjects -notcontains $_ })
$extra = @($solutionProjects | Where-Object { $_ -like 'WIP/*' -and $wipProjects -notcontains $_ })

Write-Output "solution=$SolutionPath"
Write-Output "wip_project_count=$($wipProjects.Count)"
Write-Output "solution_wip_project_count=$(@($solutionProjects | Where-Object { $_ -like 'WIP/*' }).Count)"
Write-Output "missing_from_solution=$($missing.Count)"
$missing | ForEach-Object { Write-Output "MISSING $_" }
Write-Output "solution_paths_not_on_disk=$($extra.Count)"
$extra | ForEach-Object { Write-Output "EXTRA $_" }

if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
    exit 1
}

Write-Output 'solution_coverage=ok'
exit 0
