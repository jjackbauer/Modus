[CmdletBinding()]
param(
    [string]$DataRoot = ""
)
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Workspace = Split-Path -Parent $ScriptDir
if ([string]::IsNullOrWhiteSpace($DataRoot)) { $DataRoot = Join-Path $Workspace "control-plane-data\demo-runtime" }

foreach ($name in @("server", "adapter")) {
    $pidFile = Join-Path $DataRoot "${name}.pid"
    if (-not (Test-Path $pidFile)) { Write-Host "$name.pid not found; skipping"; continue }
    $processId = [int](Get-Content $pidFile -Raw)
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -eq $process) { Write-Host "$name process $processId is not running"; Remove-Item $pidFile -Force; continue }
    Write-Host "Stopping $name process $processId"
    Stop-Process -Id $processId -Force
    Remove-Item $pidFile -Force
}