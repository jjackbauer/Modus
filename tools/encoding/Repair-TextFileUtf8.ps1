param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

if (-not (Test-Path -LiteralPath $Path)) {
    throw "File not found: $Path"
}

$bytes = [System.IO.File]::ReadAllBytes($Path)
if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
    $text = [System.Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
}
elseif ($bytes.Length -ge 4 -and $bytes[1] -eq 0 -and $bytes[3] -eq 0) {
    $text = [System.Text.Encoding]::Unicode.GetString($bytes)
}
else {
    $text = [System.IO.File]::ReadAllText($Path)
}

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($Path, $text, $utf8)