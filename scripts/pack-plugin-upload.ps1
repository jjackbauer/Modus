param(
    [Parameter(Mandatory = $true)]
    [string]$PluginAssemblyPath,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$PackageName = "plugin.upload.zip",
    [string]$SignatureName = "plugin.upload.sig"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Import-RsaFromJson([string]$KeyFilePath) {
    $keyJson = Get-Content -LiteralPath $KeyFilePath -Raw

    try {
        $keyData = $keyJson | ConvertFrom-Json
    }
    catch {
        Fail "Private key file must contain JSON with RSA parameter fields for D, DP, DQ, Exponent, InverseQ, Modulus, P, and Q."
    }

    $required = @("D", "DP", "DQ", "Exponent", "InverseQ", "Modulus", "P", "Q")
    foreach ($field in $required) {
        $value = $keyData.$field
        if ([string]::IsNullOrWhiteSpace($value)) {
            Fail "Private key JSON is missing required field '$field'."
        }
    }

    $parameters = New-Object System.Security.Cryptography.RSAParameters
    $parameters.D = [Convert]::FromBase64String($keyData.D)
    $parameters.DP = [Convert]::FromBase64String($keyData.DP)
    $parameters.DQ = [Convert]::FromBase64String($keyData.DQ)
    $parameters.Exponent = [Convert]::FromBase64String($keyData.Exponent)
    $parameters.InverseQ = [Convert]::FromBase64String($keyData.InverseQ)
    $parameters.Modulus = [Convert]::FromBase64String($keyData.Modulus)
    $parameters.P = [Convert]::FromBase64String($keyData.P)
    $parameters.Q = [Convert]::FromBase64String($keyData.Q)

    $rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportParameters($parameters)
    return $rsa
}

if (-not (Test-Path -LiteralPath $PluginAssemblyPath -PathType Leaf)) {
    Fail "Plugin assembly was not found at path '$PluginAssemblyPath'."
}

if (-not (Test-Path -LiteralPath $PrivateKeyPath -PathType Leaf)) {
    Fail "Private key file was not found at path '$PrivateKeyPath'."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$resolvedAssemblyPath = (Resolve-Path -LiteralPath $PluginAssemblyPath).Path
$resolvedKeyPath = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
$resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

$packagePath = Join-Path -Path $resolvedOutputDirectory -ChildPath $PackageName
$signaturePath = Join-Path -Path $resolvedOutputDirectory -ChildPath $SignatureName

if (Test-Path -LiteralPath $packagePath -PathType Leaf) {
    Remove-Item -LiteralPath $packagePath -Force
}

if (Test-Path -LiteralPath $signaturePath -PathType Leaf) {
    Remove-Item -LiteralPath $signaturePath -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$entryName = "plugins/$([IO.Path]::GetFileName($resolvedAssemblyPath))"
$archive = [System.IO.Compression.ZipFile]::Open($packagePath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $archive,
        $resolvedAssemblyPath,
        $entryName,
        [System.IO.Compression.CompressionLevel]::Optimal
    ) | Out-Null
}
finally {
    $archive.Dispose()
}

$packageBytes = [IO.File]::ReadAllBytes($packagePath)
$rsa = Import-RsaFromJson -KeyFilePath $resolvedKeyPath

try {
    $signatureBytes = $rsa.SignData(
        $packageBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

    [IO.File]::WriteAllBytes($signaturePath, $signatureBytes)
}
finally {
    $rsa.Dispose()
}

Write-Output "PackagePath=$packagePath"
Write-Output "SignaturePath=$signaturePath"