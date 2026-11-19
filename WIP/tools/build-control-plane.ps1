[CmdletBinding()]
param([switch]$DebugBuild)
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Workspace = Split-Path -Parent $ScriptDir
$ControlPlane = Join-Path $Workspace "control-plane"
$buildProfile = if ($DebugBuild) { "debug" } else { "release" }
$flag = if ($DebugBuild) { @() } else { @("--release") }

Push-Location $ControlPlane
try {
    cargo build @flag -p control-plane-server
    if ($LASTEXITCODE -ne 0) { throw "server build failed" }
    cargo build @flag -p control-plane-web-client --target wasm32-unknown-unknown
    if ($LASTEXITCODE -ne 0) { throw "web client build failed" }
    New-Item -ItemType Directory -Force server\static\pkg | Out-Null
    wasm-bindgen --target web --no-typescript --out-dir server\static\pkg --out-name control_plane_web "target\wasm32-unknown-unknown\$buildProfile\control_plane_web_client.wasm"
    if ($LASTEXITCODE -ne 0) { throw "wasm-bindgen failed" }
} finally { Pop-Location }

dotnet build (Join-Path $Workspace "Wip.Factory.ControlApi\Wip.Factory.ControlApi.csproj") -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "adapter build failed" }
Write-Host "Build complete. Profile: $buildProfile"