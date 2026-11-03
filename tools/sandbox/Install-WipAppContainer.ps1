#Requires -RunAsAdministrator
param([string]$ProfileName = 'WipSandboxWorker')
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}

$source = @'
using System;
using System.Runtime.InteropServices;
public static class WipAppContainerNative
{
    [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int CreateAppContainerProfile(string name, string displayName, string description, IntPtr capabilities, uint capabilityCount, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid);
    [DllImport("advapi32.dll")]
    public static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr memory);
}
'@
Add-Type -TypeDefinition $source

$sid = [IntPtr]::Zero
$hr = [WipAppContainerNative]::CreateAppContainerProfile($ProfileName, 'WIP Sandbox Worker', 'WIP AppContainer worker profile', [IntPtr]::Zero, 0, [ref]$sid)
if ($hr -ne 0) {
    $derive = [WipAppContainerNative]::DeriveAppContainerSidFromAppContainerName($ProfileName, [ref]$sid)
    if ($derive -ne 0) {
        throw ('Could not create or derive AppContainer profile {0} (create=0x{1:X8}, derive=0x{2:X8}).' -f $ProfileName, $hr, $derive)
    }
}

$sidTextPtr = [IntPtr]::Zero
if (-not [WipAppContainerNative]::ConvertSidToStringSid($sid, [ref]$sidTextPtr)) {
    throw 'Could not format the AppContainer SID.'
}
$sidText = [Runtime.InteropServices.Marshal]::PtrToStringUni($sidTextPtr)
[void][WipAppContainerNative]::LocalFree($sidTextPtr)
[void][WipAppContainerNative]::FreeSid($sid)

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workerDir = Join-Path $repo 'WIP\Wip.Sandbox.Worker\bin\Release\net10.0'
$tempRoot = Join-Path $repo '.tmp\wip-sandbox'
$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
if (-not (Test-Path $tempRoot)) { New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null }

$paths = @(
    @{ Path = $workerDir; Rights = '(OI)(CI)(RX)'; Recursive = $true },
    @{ Path = $tempRoot; Rights = '(OI)(CI)(F)'; Recursive = $true },
    @{ Path = (Join-Path $dotnetRoot 'dotnet.exe'); Rights = '(RX)'; Recursive = $false },
    @{ Path = (Join-Path $dotnetRoot 'host'); Rights = '(OI)(CI)(RX)'; Recursive = $true },
    @{ Path = (Join-Path $dotnetRoot 'shared'); Rights = '(OI)(CI)(RX)'; Recursive = $true }
)

foreach ($entry in $paths) {
    if (-not (Test-Path -LiteralPath $entry.Path)) {
        throw ('Required path is missing: {0}' -f $entry.Path)
    }

    $arguments = @($entry.Path, '/grant', ('*' + $sidText + ':' + $entry.Rights))
    if ($entry.Recursive) { $arguments += @('/T', '/C', '/Q') }
    & icacls @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw ('icacls failed for {0} with exit code {1}.' -f $entry.Path, $LASTEXITCODE)
    }
}

$evidenceDir = Join-Path $repo 'docs\execution\evidence\WP-1.4'
New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
$evidence = Join-Path $evidenceDir 'windows-appcontainer-setup.txt'
$lines = @(
    'WIP AppContainer setup',
    ('timestamp=' + (Get-Date -Format 'o')),
    ('profile=' + $ProfileName),
    ('sid=' + $sidText),
    ('workerDir=' + $workerDir),
    ('tempRoot=' + $tempRoot),
    ('dotnetRoot=' + $dotnetRoot),
    'result=ok'
)
$utf8 = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText($evidence, (($lines -join [Environment]::NewLine) + [Environment]::NewLine), $utf8)
$markerDir = Join-Path $env:ProgramData 'WIP'
New-Item -ItemType Directory -Path $markerDir -Force | Out-Null
$marker = Join-Path $markerDir 'appcontainer.sid'
[IO.File]::WriteAllText($marker, ($sidText + [Environment]::NewLine + (Get-Date -Format 'o') + [Environment]::NewLine), $utf8)

Write-Output ('AppContainer profile ready: ' + $sidText)
Write-Output ('Evidence: ' + $evidence)
