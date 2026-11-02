param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [Parameter(ParameterSetName = 'Content')]
    [string] $Content,

    [Parameter(ParameterSetName = 'ContentPath')]
    [string] $ContentPath
)

if ($PSCmdlet.ParameterSetName -eq 'ContentPath') {
    if (-not (Test-Path -LiteralPath $ContentPath)) {
        throw "Content file not found: $ContentPath"
    }

    $bytes = [System.IO.File]::ReadAllBytes($ContentPath)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $Content = [System.Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
    }
    elseif ($bytes.Length -ge 4 -and $bytes[1] -eq 0 -and $bytes[3] -eq 0) {
        $Content = [System.Text.Encoding]::Unicode.GetString($bytes)
    }
    else {
        $Content = [System.IO.File]::ReadAllText($ContentPath)
    }
}

if ([string]::IsNullOrEmpty($Content)) {
    throw 'Content is required.'
}

$utf8 = New-Object System.Text.UTF8Encoding $false
$directory = Split-Path -Parent $Path
if (-not [string]::IsNullOrWhiteSpace($directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

[System.IO.File]::WriteAllText($Path, $Content, $utf8)
