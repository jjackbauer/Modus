$ErrorActionPreference = 'Stop'
$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) { exit 1 }

function Test-LooksLikeUtf16([byte[]] $bytes) {
    if ($bytes.Length -lt 2) { return $false }
    if ($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) { return $true }
    if ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) { return $true }
    if ($bytes.Length -lt 4) { return $false }
    return ($bytes[1] -eq 0 -and $bytes[3] -eq 0)
}

$textExtensions = @(
    '.cs', '.md', '.mdc', '.json', '.csproj', '.props', '.targets', '.slnx',
    '.html', '.css', '.js', '.ts', '.tsx', '.xml', '.editorconfig', '.gitignore',
    '.yml', '.yaml', '.config', '.razor', '.cshtml', '.txt', '.sh', '.ps1'
)

$failures = @()
# Use NUL-delimited output so non-ASCII paths (for example, em dashes) are not
# C-quoted and passed to Path.GetExtension as invalid characters.
$stagedOutput = [string](git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR -z)
$staged = @($stagedOutput -split ([char]0) | Where-Object { $_ -ne '' })
foreach ($relativePath in $staged) {
    $extension = [System.IO.Path]::GetExtension($relativePath)
    if (-not $textExtensions.Contains($extension) -and -not $relativePath.EndsWith('.prompt.md')) {
        continue
    }

    $fullPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath)) { continue }

    $bytes = [System.IO.File]::ReadAllBytes($fullPath)
    if (Test-LooksLikeUtf16 $bytes) {
        $failures += $relativePath
    }
}

if ($failures.Count -gt 0) {
    Write-Error @"
UTF-16 text encoding detected in staged file(s):
$($failures -join [Environment]::NewLine)

Repair with tools/encoding/Repair-TextFileUtf8.ps1 only for already-corrupted files.

New files must be created with tools/encoding/WriteUtf8.ps1 or shell WriteAllText — never Cursor Write.
"@
    exit 1
}

exit 0
