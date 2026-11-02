param(
    [string] $Configuration = 'Release',
    [string] $ResultsDirectory = 'artifacts/ci-test-results'
)

$ErrorActionPreference = 'Continue'

$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) {
    throw 'Not inside a git repository.'
}

Set-Location $repoRoot

$projects = @(
    'WIP/Wip.Abstractions.Tests/Wip.Abstractions.Tests.csproj',
    'WIP/Wip.Artifacts.Local.Tests/Wip.Artifacts.Local.Tests.csproj',
    'WIP/Wip.Builder.Tests/Wip.Builder.Tests.csproj',
    'WIP/Wip.Modus.Tests/Wip.Modus.Tests.csproj',
    'WIP/Wip.Policy.LocalSafe.Tests/Wip.Policy.LocalSafe.Tests.csproj',
    'WIP/Wip.Runtime.Tests/Wip.Runtime.Tests.csproj',
    'WIP/Wip.Shell.Tests/Wip.Shell.Tests.csproj',
    'WIP/Wip.Tools.Shell.Tests/Wip.Tools.Shell.Tests.csproj',
    'WIP/Wip.Validation.DotNet.Tests/Wip.Validation.DotNet.Tests.csproj',
    'WIP/Wip.Workspaces.Git.Tests/Wip.Workspaces.Git.Tests.csproj',
    'WIP/Wip.Bones.Tests/Wip.Bones.Tests.csproj',
    'WIP/Wip.Bones.ModelProviders.DeepSeek.Tests/Wip.Bones.ModelProviders.DeepSeek.Tests.csproj',
    'WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj',
    'WIP/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj',
    'WIP/Wip.Bones.E2E.Tests/Wip.Bones.E2E.Tests.csproj'
)

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$summary = @(
    '# WIP offline test run',
    ('started=' + (Get-Date).ToString('o')),
    ('results=' + $ResultsDirectory),
    ''
)

foreach ($project in $projects) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    $started = Get-Date
    $output = & dotnet test $project --configuration $Configuration --no-build --blame-hang-timeout 15m --blame-hang-dump-type none --logger ('trx;LogFileName=' + $name + '.trx') --results-directory $ResultsDirectory 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $line = ('{0} EXIT {1} duration={2}' -f $name, $exitCode, ((Get-Date) - $started).ToString('hh\:mm\:ss'))
    $summary += $line
    Write-Output $line
    $outputPath = Join-Path $ResultsDirectory ($name + '.out.txt')
    [IO.File]::WriteAllText($outputPath, $output, (New-Object System.Text.UTF8Encoding $false))
}

$summary += ('finished=' + (Get-Date).ToString('o'))
[IO.File]::WriteAllText((Join-Path $ResultsDirectory 'run-summary.txt'), (($summary -join [Environment]::NewLine) + [Environment]::NewLine), (New-Object System.Text.UTF8Encoding $false))

# Test failures are intentionally not fatal here. The verifier enforces the
# waiver register, zero-test rule, and expected test counts.
exit 0
